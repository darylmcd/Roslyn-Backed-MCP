using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>One bounded native producer phase; output roles never grant input or publication authority.</summary>
internal static partial class NativeOutputPreparation
{
    internal static IEnumerable<string> RestoreInputs(EvaluatedBuildGraph.Graph graph) => graph.Nodes.SelectMany(node =>
        new[] { node.Inputs.AssetsPath, node.Inputs.ExtensionsPath is { } extension
            ? Path.Combine(extension, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json") : null }).OfType<string>();

    // Conditions may stabilize during private preparation; published authority adds their final epochs.
    internal static HashSet<string> ProtectedInputPaths(EvaluatedBuildGraph.Graph graph, ImmutableHashSet<string> mandatoryInputs)
        => mandatoryInputs.Union(graph.Nodes.SelectMany(node => node.BuildInputs.Definitions
            .Concat(node.DocumentFiles).Concat(node.ArtifactFiles).Concat(node.PathEvidence.InputFiles)
            .Concat(node.FileItems.Select(item => item.Path))))
            .Union(RestoreInputs(graph)).ToHashSet(FileSystemPath.Comparer);

    internal static bool IsDeclaredOutputDirectory(EvaluatedBuildGraph.Graph graph, string path)
    {
        foreach (var node in graph.Nodes)
        {
            var boundaries = node.PathEvidence.OutputCoordinates.Append(Path.GetDirectoryName(node.ProjectPath)!)
                .Where(boundary => !FileSystemPath.Comparer.Equals(Path.TrimEndingDirectorySeparator(boundary), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(boundary)!)));
            foreach (var directory in node.PathEvidence.Directories)
            {
                if (FileSystemPath.Comparer.Equals(path, directory)) return true;
                // Only the missing chain leading to a declared output is needed.
                // No sibling or descendant subtree acquires an output role.
                if (FileSystemPath.IsStrictDescendant(path, directory)
                    && boundaries.Any(boundary => FileSystemPath.Comparer.Equals(path, boundary) || FileSystemPath.IsStrictDescendant(boundary, path))) return true;
            }
        }
        return false;
    }

    internal static async Task AssertInputsAsync(string workspaceId, EvaluatedBuildGraph.Graph graph,
        ImmutableArray<PhysicalInputIdentity> baseline, ImmutableHashSet<string> protectedInputs, ImmutableHashSet<string> mutable,
        IWorkspacePathPolicy policy, CancellationToken ct)
    {
        var files = graph.Nodes.SelectMany(node => node.PathEvidence.OutputFiles).ToHashSet(FileSystemPath.Comparer);
        var protectedPaths = ProtectedInputPaths(graph, protectedInputs);
        await policy.ValidateAsync(workspaceId, baseline.Select(input => input.FilePath).Where(mutable.Contains).ToArray(), ct).ConfigureAwait(false);
        foreach (var before in baseline)
        {
            ct.ThrowIfCancellationRequested();
            if (mutable.Contains(before.FilePath)) await policy.ValidateAsync(workspaceId, [before.FilePath], ct).ConfigureAwait(false);
            await AssertPhysicalInputAsync(graph, before, protectedPaths, files, mutable, ct).ConfigureAwait(false);
        }
        // A later per-input policy callback can invalidate an earlier observation.
        // Renew the whole closure once, then perform only physical observations.
        await policy.ValidateAsync(workspaceId, baseline.Select(input => input.FilePath).Where(mutable.Contains).ToArray(), ct).ConfigureAwait(false);
        await AssertPhysicalInputsAsync(graph, baseline, protectedInputs, mutable, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    internal static async Task AssertPhysicalInputsAsync(EvaluatedBuildGraph.Graph graph,
        ImmutableArray<PhysicalInputIdentity> baseline, ImmutableHashSet<string> protectedInputs,
        ImmutableHashSet<string> mutable, CancellationToken ct)
    {
        var files = graph.Nodes.SelectMany(node => node.PathEvidence.OutputFiles).ToHashSet(FileSystemPath.Comparer);
        var protectedPaths = ProtectedInputPaths(graph, protectedInputs);
        foreach (var before in baseline)
            await AssertPhysicalInputAsync(graph, before, protectedPaths, files, mutable, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    private static async Task AssertPhysicalInputAsync(EvaluatedBuildGraph.Graph graph, PhysicalInputIdentity before,
        IReadOnlySet<string> protectedPaths, IReadOnlySet<string> files, ImmutableHashSet<string> mutable, CancellationToken ct)
    {
        var after = await PhysicalInputIdentity.CaptureAsync(before.FilePath, ct).ConfigureAwait(false);
        if (before.Kind == after.Kind && before.ContentHash.AsSpan().SequenceEqual(after.ContentHash.AsSpan())) return;
        if (mutable.Contains(before.FilePath) && !protectedPaths.Contains(before.FilePath))
        {
            if (IsDeclaredOutputDirectory(graph, before.FilePath) && before.Kind == PhysicalInputKind.Missing && after.Kind == PhysicalInputKind.Directory) return;
            if (files.Contains(before.FilePath) && (before.Kind is PhysicalInputKind.Missing or PhysicalInputKind.File)
                && after.Kind == PhysicalInputKind.File) return;
        }
        throw new PublicInvalidOperationException("Physical execution inputs changed: native output preparation changed an authored, dependency or undeclared physical input. Reload after resolving the native producer's input mutation.");
    }
}
