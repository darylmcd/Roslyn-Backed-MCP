using System.Collections.Immutable;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Finds only artifacts belonging to the actual evaluated path/context graph.</summary>
internal static class RestoreArtifactLocator
{
    internal static IReadOnlyList<string> Find(string loadedPath, IReadOnlyDictionary<string, string> globals, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var graph = EvaluatedBuildGraph.FromRoots([loadedPath], globals, ImmutableDictionary<string, byte[]>.Empty, ct);
            var paths = new HashSet<string>(FileSystemPath.Comparer);
            foreach (var node in graph.Nodes.Where(node => !node.IsOuterBuild))
            {
                ct.ThrowIfCancellationRequested();
                if (node.Inputs.AssetsPath is { } assets) paths.Add(assets);
                if (node.Inputs.ExtensionsPath is { } directory)
                    paths.Add(Path.Combine(directory, Path.GetFileName(node.ProjectPath) + ".nuget.dgspec.json"));
            }
            return paths.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The authoritative load reports the evaluation failure. Do not substitute
            // stale unrelated obj files into this best-effort stability probe.
            return [];
        }
    }
}
