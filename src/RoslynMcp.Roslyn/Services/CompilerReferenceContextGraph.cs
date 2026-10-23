using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Associates each declaration with the contexts selected by MSBuild's edge protocol.</summary>
internal static class CompilerReferenceContextGraph
{
    internal sealed class ValidatedContexts
    {
        private readonly Dictionary<ProjectInstance, ProjectInstance> _canonical;
        internal IReadOnlyList<ProjectInstance> Instances { get; }

        internal ValidatedContexts(Dictionary<ProjectInstance, ProjectInstance> canonical, IReadOnlyList<ProjectInstance> instances)
        {
            _canonical = canonical;
            Instances = instances;
        }

        internal ProjectInstance Canonical(ProjectInstance instance) => _canonical.TryGetValue(instance, out var canonical)
            ? canonical : throw new PublicInvalidOperationException("A compiler context was not part of the validated evaluated graph.");

        public override string ToString() => $"ValidatedCompilerContexts {{ Count = {Instances.Count} }}";
    }

    private sealed class ContextSnapshot(ProjectInstance instance, BuildPathRoles.Evidence evidence, NativeTaskRegistrations registrations)
    {
        private readonly Dictionary<string, string> _properties = instance.Properties.ToDictionary(
            property => property.Name, property => property.EvaluatedValue, StringComparer.OrdinalIgnoreCase);
        // The public conversion includes item definitions, all item metadata, targets,
        // toolset, and initial/default targets. Full Properties additionally retain
        // reserved, global, and environment values omitted by that conversion.
        private readonly string _definition = instance.ToProjectRootElement().RawXml;
        private readonly string[] _imports = instance.ImportPaths.ToArray();
        private readonly BuildPathRoles.Evidence _evidence = evidence;
        private readonly NativeTaskRegistrations _registrations = registrations;

        internal bool EquivalentTo(ProjectInstance other, BuildPathRoles.Evidence otherEvidence, NativeTaskRegistrations otherRegistrations) =>
            SameGlobals(_properties, other.Properties.ToDictionary(property => property.Name,
                property => property.EvaluatedValue, StringComparer.OrdinalIgnoreCase))
            && _definition == other.ToProjectRootElement().RawXml
            && _imports.SequenceEqual(other.ImportPaths, FileSystemPath.Comparer)
            && _evidence.EquivalentTo(otherEvidence)
            && _registrations.Equals(otherRegistrations);

        public override string ToString() => "EvaluatedCompilerContextSnapshot";
    }

    internal static ValidatedContexts ValidateContexts(IReadOnlyCollection<ProjectInstance> evaluated,
        Func<ProjectInstance, BuildPathRoles.Evidence> pathEvidence,
        Func<ProjectInstance, NativeTaskRegistrations> taskRegistrations, CancellationToken ct)
    {
        // Only repeated identities require a full evaluated-definition comparison.
        // Determine those groups before observing any evidence callback, then retain
        // the first occurrence's snapshot at its original validation boundary.
        var occurrences = new List<(ProjectInstance Instance, (string Path, ImmutableDictionary<string, string> Globals) Key)>();
        var seen = new HashSet<(string Path, ImmutableDictionary<string, string> Globals)>(EvaluatedBuildGraph.ContextIdentityComparer.Instance);
        var repeated = new HashSet<(string Path, ImmutableDictionary<string, string> Globals)>(EvaluatedBuildGraph.ContextIdentityComparer.Instance);
        foreach (var instance in evaluated)
        {
            ct.ThrowIfCancellationRequested();
            var key = (Path.GetFullPath(instance.FullPath), instance.GlobalProperties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase));
            occurrences.Add((instance, key));
            if (!seen.Add(key)) repeated.Add(key);
        }
        var identities = new Dictionary<(string Path, ImmutableDictionary<string, string> Globals),
            (ProjectInstance Instance, ContextSnapshot? Snapshot)>(EvaluatedBuildGraph.ContextIdentityComparer.Instance);
        var canonical = new Dictionary<ProjectInstance, ProjectInstance>(ReferenceEqualityComparer.Instance);
        foreach (var (instance, key) in occurrences)
        {
            ct.ThrowIfCancellationRequested();
            if (identities.TryGetValue(key, out var existing))
            {
                if (existing.Snapshot is not { } snapshot || !snapshot.EquivalentTo(instance, pathEvidence(instance), taskRegistrations(instance)))
                    throw new PublicInvalidOperationException("The same compiler build identity evaluated inconsistently.");
                canonical.Add(instance, existing.Instance);
            }
            else
            {
                var evidence = pathEvidence(instance);
                var registrations = taskRegistrations(instance);
                identities.Add(key, (instance, repeated.Contains(key) ? new ContextSnapshot(instance, evidence, registrations) : null));
                canonical.Add(instance, instance);
            }
        }
        return new(canonical, Array.AsReadOnly(identities.Values.Select(value => value.Instance).ToArray()));
    }

    internal static IReadOnlyList<ImmutableArray<CompilerProjectReferenceTarget>> Capture(
        ProjectInstance owner, ValidatedContexts evaluated, CancellationToken ct)
    {
        owner = evaluated.Canonical(owner);
        var declarations = owner.GetItems("ProjectReference").ToArray();
        var result = new List<ImmutableArray<CompilerProjectReferenceTarget>>(declarations.Length);
        for (var index = 0; index < declarations.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var selected = index;
            var marker = "RoslynMcpDirectReference" + Guid.NewGuid().ToString("N");
            var globals = owner.GlobalProperties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            globals.TryAdd("IsGraphBuild", marker);
            using var collection = new Microsoft.Build.Evaluation.ProjectCollection();
            var originals = new ConcurrentDictionary<ProjectInstance, ProjectInstance>(ReferenceEqualityComparer.Instance);
            // Interpret already captured instances on this caller. Spawning native graph
            // workers for these local copies adds scheduling and a spinning waiter to
            // every direct-edge query without any parallel evaluation or IO to perform.
            var graph = new ProjectGraph([new ProjectGraphEntryPoint(owner.FullPath, globals)], collection, (path, properties, _) =>
            {
                ct.ThrowIfCancellationRequested();
                var actualProperties = properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                if (actualProperties.TryGetValue("IsGraphBuild", out var protocol) && protocol == marker) actualProperties.Remove("IsGraphBuild");
                var matches = evaluated.Instances.Where(instance => FileSystemPath.Comparer.Equals(instance.FullPath, path)
                    && SameGlobals(instance.GlobalProperties, actualProperties)).ToArray();
                if (matches.Length != 1)
                    throw new PublicInvalidOperationException("A direct compiler edge has no unique captured evaluated context.");
                var original = matches[0];
                var interpretation = original.DeepCopy(false);
                // GraphBuilder adds these synthetic edges only after evaluating the project.
                // Keep that graph-only switch out of the original instance and command globals.
                interpretation.SetProperty("AddTransitiveProjectReferencesInStaticGraph", "false");
                if (ReferenceEquals(original, owner))
                {
                    var references = interpretation.GetItems("ProjectReference").ToArray();
                    for (var item = 0; item < references.Length; item++)
                        if (item != selected) interpretation.RemoveItem(references[item]);
                }
                if (!originals.TryAdd(interpretation, original))
                    throw new PublicInvalidOperationException("A compiler graph interpretation was registered more than once.");
                return interpretation;
            }, degreeOfParallelism: 1, cancellationToken: ct);
            var targetPath = declarations[index].GetMetadataValue("FullPath");
            var targets = graph.EntryPointNodes.Single().ProjectReferences
                .Select(node => originals[node.ProjectInstance])
                .Where(instance => FileSystemPath.Comparer.Equals(instance.FullPath, targetPath)
                    && !IsOuterBuild(instance))
                .Distinct<ProjectInstance>(ReferenceEqualityComparer.Instance)
                .Select(instance => new CompilerProjectReferenceTarget(instance.FullPath,
                    AbsoluteOutput(instance), instance.GetPropertyValue("TargetFramework"),
                    instance.GlobalProperties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)))
                .ToImmutableArray();
            result.Add(targets);
        }
        return result;
    }

    private static bool SameGlobals(IDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static string? AbsoluteOutput(ProjectInstance instance) => instance.GetPropertyValue("TargetPath") is { Length: > 0 } output
        ? NativeItemPath.FullPath(output, Path.GetDirectoryName(instance.FullPath)!) : null;

    private static bool IsOuterBuild(ProjectInstance instance)
    {
        var property = instance.GetPropertyValue("InnerBuildProperty");
        var values = instance.GetPropertyValue("InnerBuildPropertyValues");
        return property.Length > 0 && instance.GetPropertyValue(property).Length == 0
            && values.Length > 0 && instance.GetPropertyValue(values).Length > 0;
    }
}
