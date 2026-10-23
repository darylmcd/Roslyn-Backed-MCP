using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Snapshots package roots before restore can rewrite assets and isolates mixed roots.</summary>
internal static class RestoreInvocationPlanner
{
    internal readonly record struct ProjectRestore(string ProjectPath, string? PackagesPath, string? TargetFramework,
        System.Collections.Immutable.ImmutableDictionary<string, string> EvaluationProperties, string? AssetsPath)
    {
        public override string ToString() => $"ProjectRestore {{ HasPackageRoot = {PackagesPath is not null}, HasTargetFramework = {TargetFramework is not null}, HasAssetsPath = {AssetsPath is not null}, GlobalPropertyCount = {EvaluationProperties?.Count ?? 0} }}";
    }
    internal sealed record Invocation(string TargetPath, IReadOnlyList<string> Arguments, ProjectRestore? Context);

    internal static IReadOnlyList<ProjectRestore> Capture(IEnumerable<string> projectPaths, IReadOnlyDictionary<string, string> globals, CancellationToken ct) =>
        CaptureNodes(EvaluatedBuildGraph.FromRoots(projectPaths, globals,
            System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, ct).Nodes, ct);

    internal static IReadOnlyList<ProjectRestore> Capture(Solution solution, CancellationToken ct) => CaptureNodes(EvaluatedBuildGraph.Source(solution, ct).Nodes, ct);

    internal sealed record SelectedGraph(EvaluatedBuildGraph.Node Root, IReadOnlyList<EvaluatedBuildGraph.Node> Nodes);

    internal static IReadOnlyList<ProjectRestore> Capture(Project project, CancellationToken ct) => CaptureNodes(Select(project, EvaluatedBuildGraph.Source(project.Solution, ct), ct).Nodes, ct);

    internal static SelectedGraph Select(Project project, EvaluatedBuildGraph.Graph graph, CancellationToken ct)
    {
        var selected = EvaluatedBuildGraph.Resolve(project, graph);
        var closure = new HashSet<EvaluatedBuildGraph.Node>();
        var pending = new Stack<EvaluatedBuildGraph.Node>();
        pending.Push(selected);
        while (pending.TryPop(out var node))
        {
            ct.ThrowIfCancellationRequested();
            if (closure.Add(node))
            {
                foreach (var dependency in node.Dependencies) pending.Push(dependency);
                // A targeted restore must not erase another loaded context's shared assets.
                if (node.Inputs.AssetsPath is { } assets)
                    foreach (var sibling in graph.Nodes.Where(candidate => !candidate.IsOuterBuild
                        && FileSystemPath.Comparer.Equals(candidate.Inputs.AssetsPath, assets))) pending.Push(sibling);
            }
        }
        return new(selected, graph.Nodes.Where(closure.Contains).ToArray());
    }

    internal static IReadOnlyList<ProjectRestore> CaptureNodes(IEnumerable<EvaluatedBuildGraph.Node> nodes, CancellationToken ct) =>
        nodes.Where(node => !node.IsOuterBuild).Select(node => new ProjectRestore(node.ProjectPath,
            RestoreStalenessDetector.ReadRequiredRestorePackagesPathFromAssets(node.Inputs.AssetsPath, ct),
            node.Globals.GetValueOrDefault("TargetFramework"), node.Globals, node.Inputs.AssetsPath)).ToArray();

    internal static IReadOnlyList<Invocation> Plan(string targetPath, IReadOnlyList<ProjectRestore> projects)
    {
        projects = CoalesceSharedAssets(projects);
        // These globals describe evaluated project nodes, not a solution-entry context.
        // In particular, project Platform=AnyCPU is not the solution's "Any CPU".
        // Retain topological order and prevent one node from restoring another's cache.
        return projects.Count == 0
            ? [BuildInvocation(targetPath, null, noDependencies: false, targetFramework: null,
                System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, null)]
            : projects.Select(project => BuildInvocation(project.ProjectPath, project.PackagesPath, noDependencies: true,
                project.TargetFramework, project.EvaluationProperties, project)).ToArray();
    }

    private static IReadOnlyList<ProjectRestore> CoalesceSharedAssets(IReadOnlyList<ProjectRestore> projects)
    {
        var result = new List<ProjectRestore>();
        var handled = new HashSet<string>(FileSystemPath.Comparer);
        foreach (var project in projects)
        {
            if (project.AssetsPath is null) { result.Add(project); continue; }
            var assets = Path.GetFullPath(project.AssetsPath);
            if (!handled.Add(assets)) continue;
            var group = projects.Where(candidate => candidate.AssetsPath is not null
                && FileSystemPath.Comparer.Equals(Path.GetFullPath(candidate.AssetsPath), assets)).ToArray();
            if (group.Length == 1) { result.Add(project); continue; }
            var context = project.EvaluationProperties.Remove("TargetFramework");
            if (group.Any(candidate => !FileSystemPath.Comparer.Equals(Path.GetFullPath(candidate.ProjectPath), Path.GetFullPath(project.ProjectPath))
                || !FileSystemPath.Comparer.Equals(candidate.PackagesPath, project.PackagesPath)
                || !SameProperties(context, candidate.EvaluationProperties.Remove("TargetFramework")))) throw SharedAssetsRefusal();
            if (group.All(candidate => candidate.TargetFramework == project.TargetFramework
                && SameProperties(candidate.EvaluationProperties, project.EvaluationProperties)))
            { result.Add(project); continue; }
            if (group.Any(candidate => string.IsNullOrWhiteSpace(candidate.TargetFramework))) throw SharedAssetsRefusal();
            var selected = group.Select(candidate => candidate.TargetFramework!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outer = MsBuildMetadataHelper.EvaluateProperties(project.ProjectPath, context, "TargetFrameworks")[0]
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Count != group.Length || !selected.SetEquals(outer)) throw SharedAssetsRefusal();
            // NuGet globally supplied TargetFramework overrides TargetFrameworks. Restore
            // the proven complete outer set once, retaining every other exact global.
            result.Add(project with { TargetFramework = null, EvaluationProperties = context });
        }
        return result;
    }

    private static bool SameProperties(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static PublicInvalidOperationException SharedAssetsRefusal() => new(
        "Distinct evaluated restore contexts share one assets file and cannot be preserved together. Configure separate ProjectAssetsFile or MSBuildProjectExtensionsPath values, or select one context and reload.");

    private static Invocation BuildInvocation(string targetPath, string? packagesPath, bool noDependencies, string? targetFramework,
        IReadOnlyDictionary<string, string> properties, ProjectRestore? project)
    {
        var arguments = new List<string> { "restore", targetPath, "--nologo" };
        if (packagesPath is not null)
        {
            arguments.Add("--packages");
            arguments.Add(packagesPath);
        }
        if (noDependencies)
            arguments.Add("--no-dependencies");
        var context = properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (targetFramework is not null) context["TargetFramework"] = targetFramework;
        return new Invocation(targetPath, MsBuildCommandProperties.Append(arguments, context), project);
    }
}
