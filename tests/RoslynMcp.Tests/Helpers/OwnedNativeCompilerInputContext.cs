using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Uses production tool provenance and authored-root precedence for direct owned-native producer fixtures.</summary>
internal static class OwnedNativeCompilerInputContext
{
    internal static string CaptureToolsVersion(string path, IReadOnlyDictionary<string, string> globals)
    {
        var graph = EvaluatedBuildGraph.FromRoots([path], globals,
            System.Collections.Immutable.ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
        return graph.Nodes.Where(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, path))
            .Select(node => node.TaskRegistrations.ToolsVersion).Distinct(StringComparer.OrdinalIgnoreCase).Single();
    }

    internal static CompilerArgumentInputContext Create(Project project)
    {
        var path = project.FilePath ?? throw new AssertFailedException("The compiler fixture has no physical project.");
        var tools = BuildInputProvenance.ToolRoots();
        var authored = project.Solution.Projects.Select(owner => Path.GetDirectoryName(owner.FilePath)
            ?? throw new AssertFailedException("An authored compiler fixture has no directory.")).ToArray();
        var policy = new OwnedTestWorkspacePathPolicy();
        var provenance = new ImmutablePathProvenance(tools, [], authored);
        async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
        {
            await policy.ValidateAsync("native-compiler-fixture", paths.Where(input => !provenance.IsImmutable(input)).ToArray(), ct);
            foreach (var input in paths) WorkspaceManager.EnsurePhysicalPath(input);
        }
        async Task<byte[]?> Read(string input, CancellationToken ct)
        {
            await Authorize([input], ct);
            return await FileSnapshotCapture.ReadIfPresentAsync(input, File.ReadAllBytesAsync, ct);
        }
        var membership = NativeBuildMembership.Capture(EvaluatedBuildGraph.Source(project.Solution, CancellationToken.None), CancellationToken.None);
        return new(Path.GetDirectoryName(path) ?? throw new AssertFailedException("The compiler fixture has no directory."), Authorize, Read,
            (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken),
            CompilerArgumentInputContext.CreateStagePath,
            (stage, ct) => membership.AssertTemporaryPathDisjointAsync(stage, [], Authorize, ct));
    }
}
