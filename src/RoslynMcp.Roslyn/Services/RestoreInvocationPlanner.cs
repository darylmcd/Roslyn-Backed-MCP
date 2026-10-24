using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Captures the entire evaluated restore inventory before any command can change assets.</summary>
internal static class RestoreInvocationPlanner
{
    internal sealed record Invocation(string TargetPath, ImmutableArray<string> Arguments);

    internal static TimeSpan RemainingBudget(TimeSpan timeout, TimeSpan elapsed, TimeSpan reserve, string timeoutMessage)
    {
        var remaining = timeout - elapsed;
        if (RequestDeadline.Remaining is { } requestRemaining)
        {
            remaining = TimeSpan.FromTicks(Math.Min(remaining.Ticks, (requestRemaining - reserve).Ticks));
        }
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException(timeoutMessage);
    }

    internal static ImmutableArray<Invocation> Capture(string targetPath, Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phase.CancelAfter(remainingBudget());
        MsBuildInitializer.EnsureInitialized();
        try
        {
            return CaptureCore(targetPath, remainingBudget, phase.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("The restore inventory exceeded the remaining restore time budget.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not TimeoutException and not PublicInvalidOperationException)
        {
            throw new PublicInvalidOperationException(
                "The required project inventory could not be evaluated for restore. Correct the project evaluation errors and retry.",
                InvalidOperationErrors.Internal("Could not capture the evaluated restore inventory.", ex));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ImmutableArray<Invocation> CaptureCore(string targetPath, Func<TimeSpan> remainingBudget, CancellationToken ct)
    {
        using var collection = new ProjectCollection();
        // GraphBuilder injects IsGraphBuild=true when it is absent. These source commands are
        // ordinary restores, so that engine-only property must never affect evaluation. A private
        // marker prevents injection and is removed only from a COPY supplied to the evaluator.
        // Actual reference-derived properties (including an explicit IsGraphBuild) are preserved.
        // The graph is used solely for inventory, never built; its opaque keys remain untouched.
        var graphMarker = Guid.NewGuid().ToString("N");
        var entryPoints = new List<ProjectGraphEntryPoint>
        {
            CreateEntryPoint(targetPath, MsBuildMetadataHelper.DefaultGlobalProperties),
        };

        ProjectGraph graph;
        while (true)
        {
            CheckBudget();
            graph = new ProjectGraph(entryPoints, collection, CreateInstance, degreeOfParallelism: 1, cancellationToken: ct);
            var contexts = graph.ProjectNodes.Select(n => ContextOf(n.ProjectInstance)).ToHashSet(ContextComparer.Instance);
            var missingOuterContexts = graph.ProjectNodes
                .Where(n => IsFrameworkInner(n.ProjectInstance))
                .Select(n => RestoreContextOf(n.ProjectInstance))
                .Distinct(ContextComparer.Instance)
                .Where(c => !contexts.Contains(c)).ToArray();
            if (missingOuterContexts.Length == 0) break;

            // A reference can select one inner framework of a multi-target dependency. Restoring
            // that inner alone would truncate its assets file. Expand its full outer context too,
            // discovering every framework's references before planning isolated execution.
            entryPoints = graph.EntryPointNodes.Select(n => ContextOf(n.ProjectInstance))
                .Concat(missingOuterContexts).Distinct(ContextComparer.Instance)
                .Select(c => CreateEntryPoint(c.Path, c.Properties)).ToList();
        }

        var units = new Dictionary<Context, RestoreUnit>(ContextComparer.Instance);
        var nodesByContext = graph.ProjectNodes.ToDictionary(n => ContextOf(n.ProjectInstance), ContextComparer.Instance);
        var assetsByContext = nodesByContext.Keys.ToDictionary(c => c,
            c => ProjectMetadataParser.GetExistingAssetsPath(c.Path, c.Properties), ContextComparer.Instance);
        var unitContexts = new Dictionary<Context, Context>(ContextComparer.Instance);
        var rootsByAssets = new Dictionary<string, string?>(FileSystemPath.Comparer);
        foreach (var node in graph.ProjectNodes)
        {
            CheckBudget();
            var context = ContextOf(node.ProjectInstance);
            var root = RestoreStalenessDetector.TryReadRestorePackagesPath(context.Path, context.Properties);
            if (root is not null) root = Path.GetFullPath(root, Path.GetDirectoryName(context.Path)!);
            CheckBudget();
            var assets = assetsByContext[context];
            if (assets is not null)
            {
                if (rootsByAssets.TryGetValue(assets, out var previousRoot) && !FileSystemPath.Comparer.Equals(root, previousRoot))
                {
                    throw new PublicInvalidOperationException(
                        "Required restore contexts record incompatible package folders in the same assets file. Restore those contexts to separate assets files and retry.");
                }
                rootsByAssets[assets] = root;
            }
            var restoreContext = RestoreContextOf(node.ProjectInstance);
            // Only collapse an inner build when its assets are the outer build's file.
            // A framework-specific assets file is an independent restore unit; retain its
            // framework and recorded root instead of replacing it with the outer root.
            if (!ContextComparer.Instance.Equals(context, restoreContext) &&
                !FileSystemPath.Comparer.Equals(assets, assetsByContext[restoreContext]))
            {
                restoreContext = context;
            }
            unitContexts.Add(context, restoreContext);
            if (ContextComparer.Instance.Equals(context, restoreContext)) units.TryAdd(context, new RestoreUnit(context, root));
        }

        foreach (var node in graph.ProjectNodes)
        {
            CheckBudget();
            var context = ContextOf(node.ProjectInstance);
            var outer = RestoreContextOf(node.ProjectInstance);
            var unit = units[unitContexts[context]];
            if (!ContextComparer.Instance.Equals(unit.Context, outer)) unit.Dependencies.Add(outer);
            foreach (var reference in node.ProjectReferences)
            {
                var referenceOuter = RestoreContextOf(reference.ProjectInstance);
                // Outer-to-inner graph edges describe one project's frameworks, not a
                // project dependency. Independent inner assets are restored after the outer.
                if (ContextComparer.Instance.Equals(outer, referenceOuter)) continue;
                foreach (var dependency in units.Keys.Where(c =>
                    ContextComparer.Instance.Equals(RestoreContextOf(nodesByContext[c].ProjectInstance), referenceOuter)))
                {
                    unit.Dependencies.Add(dependency);
                    units[outer].Dependencies.Add(dependency);
                }
            }
        }

        var roots = units.Values.Select(u => u.PackagesPath).Distinct(FileSystemPath.Comparer).ToArray();
        if (roots.Length <= 1)
        {
            CheckBudget();
            return [CreateInvocation(targetPath, roots.SingleOrDefault(), MsBuildMetadataHelper.DefaultGlobalProperties, isolated: false)];
        }

        var invocations = ImmutableArray.CreateBuilder<Invocation>();
        var visited = new HashSet<Context>(ContextComparer.Instance);
        var visiting = new HashSet<Context>(ContextComparer.Instance);
        foreach (var unit in units.Values.OrderBy(u => u.Context.Path, FileSystemPath.Comparer).ThenBy(u => u.Context.Key, StringComparer.Ordinal))
        {
            Visit(unit);
        }
        return invocations.ToImmutable();

        void Visit(RestoreUnit unit)
        {
            CheckBudget();
            if (visited.Contains(unit.Context)) return;
            if (!visiting.Add(unit.Context))
            {
                throw InvalidOperationErrors.Internal("The full-framework restore inventory contains a dependency cycle.");
            }
            foreach (var dependency in unit.Dependencies.OrderBy(c => c.Path, FileSystemPath.Comparer).ThenBy(c => c.Key, StringComparer.Ordinal))
            {
                Visit(units[dependency]);
            }
            visiting.Remove(unit.Context);
            visited.Add(unit.Context);
            invocations.Add(CreateInvocation(unit.Context.Path, unit.PackagesPath, unit.Context.Properties, isolated: true));
        }

        void CheckBudget()
        {
            ct.ThrowIfCancellationRequested();
            remainingBudget();
        }

        ProjectGraphEntryPoint CreateEntryPoint(string path, IReadOnlyDictionary<string, string> properties)
        {
            var graphProperties = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
            if (!graphProperties.ContainsKey("IsGraphBuild")) graphProperties.Add("IsGraphBuild", graphMarker);
            return new ProjectGraphEntryPoint(path, graphProperties);
        }

        ProjectInstance CreateInstance(string path, Dictionary<string, string> properties, ProjectCollection projectCollection)
        {
            CheckBudget();
            var commandProperties = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
            if (commandProperties.TryGetValue("IsGraphBuild", out var value) && value == graphMarker) commandProperties.Remove("IsGraphBuild");
            return new ProjectInstance(path, commandProperties, toolsVersion: null, projectCollection);
        }
    }

    private static bool IsFrameworkInner(ProjectInstance project) =>
        project.GlobalProperties.ContainsKey("TargetFramework") && !string.IsNullOrWhiteSpace(project.GetPropertyValue("TargetFrameworks"));

    private static Context ContextOf(ProjectInstance project) => new(Path.GetFullPath(project.FullPath),
        project.GlobalProperties.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase));

    private static Context RestoreContextOf(ProjectInstance project)
    {
        var context = ContextOf(project);
        return IsFrameworkInner(project) ? new Context(context.Path, context.Properties.Remove("TargetFramework")) : context;
    }

    private static Invocation CreateInvocation(string target, string? root, IReadOnlyDictionary<string, string> properties, bool isolated)
    {
        var argv = ImmutableArray.CreateBuilder<string>();
        argv.Add("restore");
        argv.Add(target);
        argv.Add("--nologo");
        if (root is not null)
        {
            argv.Add("--packages");
            argv.Add(root);
        }
        if (isolated) argv.Add("--no-dependencies");
        foreach (var property in properties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            argv.Add($"-p:{property.Key}={ProjectCollection.Escape(property.Value)}");
        }
        return new Invocation(target, argv.ToImmutable());
    }

    private sealed record Context(string Path, ImmutableSortedDictionary<string, string> Properties)
    {
        internal string Key { get; } = string.Join("\n", Properties.Select(p => $"{p.Key.ToUpperInvariant()}:{p.Value.Length}:{p.Value}"));
    }

    private sealed class ContextComparer : IEqualityComparer<Context>
    {
        internal static ContextComparer Instance { get; } = new();
        public bool Equals(Context? x, Context? y) => ReferenceEquals(x, y) ||
            x is not null && y is not null && FileSystemPath.Comparer.Equals(x.Path, y.Path) && x.Key == y.Key;
        public int GetHashCode(Context obj) => HashCode.Combine(FileSystemPath.Comparer.GetHashCode(obj.Path), StringComparer.Ordinal.GetHashCode(obj.Key));
    }

    private sealed record RestoreUnit(Context Context, string? PackagesPath)
    {
        internal HashSet<Context> Dependencies { get; } = new(ContextComparer.Instance);
    }
}
