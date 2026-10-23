using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal sealed class WorkspaceCacheCoordinator
{
    private readonly IWorkspaceCacheStore _cacheStore;
    private readonly ILogger? _logger;

    public WorkspaceCacheCoordinator(IWorkspaceCacheStore cacheStore, ILogger? logger = null)
    {
        _cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
        _logger = logger;
    }

    /// <summary>Reads a current-authorized entry hash and an owned-cache candidate; cached paths confer no read authority.</summary>
    public async Task<WorkspaceCacheProbe?> TryProbeAsync(string fullPath,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var solutionHash = await ComputeSolutionContentHashAsync(fullPath, authorize, ct).ConfigureAwait(false);
        if (solutionHash is null) return null;
        var sdkVersion = ResolveSdkVersionForCacheKey();
        var candidate = await TryEnumerateNewestCacheEntryAsync(solutionHash, sdkVersion, ct).ConfigureAwait(false);
        return new WorkspaceCacheProbe(solutionHash, sdkVersion, candidate);
    }

    /// <summary>Validates a candidate against actual graph/reference coordinates before observing only current loaded references.</summary>
    public async Task<WorkspaceCacheWriteResult?> ResolveAndWriteAsync(Solution solution, WorkspaceCacheProbe probe,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var actualGraph = BuildCachedGraphFromSolution(solution);
        var actualGraphHash = ComputeMsbuildGraphHash(actualGraph);
        var coordinateMatch = probe.CachedEntry is { } candidate
            && string.Equals(ComputeMsbuildGraphHash(candidate.ProjectGraph), actualGraphHash, StringComparison.Ordinal)
            && MetadataCoordinatesMatch(candidate.MetadataReferences, solution);
        var actualMetadata = await BuildCachedMetadataRefsFromSolutionAsync(solution, authorize, ct).ConfigureAwait(false);
        if (actualMetadata is null) return null;
        var cacheHit = coordinateMatch && MetadataTimestampsMatch(probe.CachedEntry!.MetadataReferences, actualMetadata);
        var key = new WorkspaceCacheKey(probe.SolutionHash, probe.SdkVersion, actualGraphHash);
        return await PutAsync(key, new(actualGraph, actualMetadata, DateTime.UtcNow), cacheHit, ct).ConfigureAwait(false);
    }

    public async Task<WorkspaceCacheWriteResult?> WriteFreshEntryAsync(Solution solution, string fullPath,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var solutionHash = await ComputeSolutionContentHashAsync(fullPath, authorize, ct).ConfigureAwait(false);
        if (solutionHash is null) return null;
        var graph = BuildCachedGraphFromSolution(solution);
        var metadata = await BuildCachedMetadataRefsFromSolutionAsync(solution, authorize, ct).ConfigureAwait(false);
        if (metadata is null) return null;
        var key = new WorkspaceCacheKey(solutionHash, ResolveSdkVersionForCacheKey(), ComputeMsbuildGraphHash(graph));
        return await PutAsync(key, new(graph, metadata, DateTime.UtcNow), cacheHit: false, ct).ConfigureAwait(false);
    }

    private async Task<WorkspaceCacheWriteResult?> PutAsync(WorkspaceCacheKey key, WorkspaceCacheEntry entry, bool cacheHit, CancellationToken ct)
    {
        try
        {
            _ = await _cacheStore.PutAsync(key, entry, ct).ConfigureAwait(false);
            return new(cacheHit);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Only cache-owned IO is best effort. Current-input authorization is outside this catch.
            _logger?.LogDebug(failure, "Workspace cache write failed; current native load remains available.");
            return null;
        }
    }

    /// <summary>
    /// Enumerates cache entries under the concrete store's solution/sdk directory and returns
    /// the most recently written entry. Custom store implementations cannot expose a root
    /// layout, so they skip this probe and still receive post-load writes through the interface.
    /// </summary>
    private async Task<WorkspaceCacheEntry?> TryEnumerateNewestCacheEntryAsync(
        string solutionHash,
        string sdkVersion,
        CancellationToken ct)
    {
        if (_cacheStore is not WorkspaceCacheStore concreteStore)
        {
            return null;
        }

        var probeKey = new WorkspaceCacheKey(solutionHash, sdkVersion, "probe");
        var probePath = concreteStore.ResolveEntryPath(probeKey);
        var graphHashDir = Path.GetDirectoryName(probePath);
        var sdkDir = graphHashDir is null ? null : Path.GetDirectoryName(graphHashDir);

        if (sdkDir is null || !Directory.Exists(sdkDir))
        {
            return null;
        }

        try
        {
            string? newestEntryPath = null;
            DateTime newestMtime = DateTime.MinValue;

            foreach (var graphDir in Directory.EnumerateDirectories(sdkDir))
            {
                ct.ThrowIfCancellationRequested();
                var entryPath = Path.Combine(graphDir, "entry.json");
                if (!File.Exists(entryPath)) continue;
                var mtime = File.GetLastWriteTimeUtc(entryPath);
                if (mtime > newestMtime)
                {
                    newestMtime = mtime;
                    newestEntryPath = entryPath;
                }
            }

            if (newestEntryPath is null) return null;

            return await concreteStore.TryGetEntryAtPathAsync(newestEntryPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Workspace cache enumeration failed under '{Dir}'; treating as miss.", sdkDir);
            return null;
        }
    }

    internal static async Task<string?> ComputeSolutionContentHashAsync(string fullPath,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        await authorize([fullPath], ct).ConfigureAwait(false);
        try
        {
            var bytes = await PhysicalInputReader.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string ResolveSdkVersionForCacheKey() =>
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    internal static string ComputeMsbuildGraphHash(IReadOnlyList<CachedProjectGraphNode> graph)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in graph.OrderBy(n => n.ProjectPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(node.ProjectPath);
            sb.Append('\t');
            foreach (var refPath in node.ProjectReferences.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(refPath);
                sb.Append('|');
            }
            sb.Append('\n');
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static IReadOnlyList<CachedProjectGraphNode> BuildCachedGraphFromSolution(Solution solution)
    {
        var byId = solution.Projects.ToDictionary(p => p.Id);
        var graph = new List<CachedProjectGraphNode>(solution.ProjectIds.Count);
        foreach (var project in solution.Projects.OrderBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(project.FilePath)) continue;
            var refs = new List<string>(project.ProjectReferences.Count());
            foreach (var pref in project.ProjectReferences)
            {
                if (byId.TryGetValue(pref.ProjectId, out var refProj) && !string.IsNullOrEmpty(refProj.FilePath))
                {
                    refs.Add(refProj.FilePath);
                }
            }
            graph.Add(new CachedProjectGraphNode(project.FilePath, refs));
        }
        return graph;
    }

    internal static async Task<IReadOnlyList<CachedProjectMetadataReferences>?> BuildCachedMetadataRefsFromSolutionAsync(
        Solution solution, Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        var projects = solution.Projects.Where(project => !string.IsNullOrEmpty(project.FilePath))
            .OrderBy(project => project.FilePath, FileSystemPath.Comparer).ToArray();
        var paths = projects.SelectMany(project => project.MetadataReferences).OfType<PortableExecutableReference>()
            .Select(reference => reference.FilePath).OfType<string>().Where(path => path.Length != 0)
            .Distinct(FileSystemPath.Comparer).ToArray();
        await authorize(paths, ct).ConfigureAwait(false);
        // No awaited work separates this exact batch grant from the held-file metadata snapshot.
        var timestamps = new Dictionary<string, DateTime>(FileSystemPath.Comparer);
        try
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(path, ct) is not { } timestamp) return null;
                timestamps.Add(path, timestamp);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // An incomplete selected-reference snapshot cannot validate or refresh a cache entry.
            return null;
        }
        return projects.Select(project => new CachedProjectMetadataReferences(project.FilePath!,
            project.MetadataReferences.OfType<PortableExecutableReference>().Select(reference => reference.FilePath)
                .OfType<string>().Where(timestamps.ContainsKey).Select(path => new CachedMetadataReference(path, timestamps[path])).ToArray())).ToArray();
    }

    private static bool MetadataCoordinatesMatch(IReadOnlyList<CachedProjectMetadataReferences> cached, Solution actual)
    {
        var expected = Coordinates(cached);
        var observed = Coordinates(actual.Projects.Where(project => !string.IsNullOrEmpty(project.FilePath))
            .Select(project => new CachedProjectMetadataReferences(project.FilePath!, project.MetadataReferences
                .OfType<PortableExecutableReference>().Select(reference => reference.FilePath).OfType<string>()
                .Where(path => path.Length != 0).Select(path => new CachedMetadataReference(path, default)).ToArray())).ToArray());
        return expected.Count == observed.Count && expected.All(pair => observed.TryGetValue(pair.Key, out var paths) && pair.Value.SetEquals(paths));
    }

    private static Dictionary<string, HashSet<string>> Coordinates(IReadOnlyList<CachedProjectMetadataReferences> metadata)
    {
        var result = new Dictionary<string, HashSet<string>>(FileSystemPath.Comparer);
        foreach (var project in metadata)
            foreach (var reference in project.References)
            {
                if (!result.TryGetValue(project.ProjectPath, out var paths)) result.Add(project.ProjectPath, paths = new(FileSystemPath.Comparer));
                paths.Add(reference.AssemblyPath);
            }
        return result;
    }

    private static bool MetadataTimestampsMatch(IReadOnlyList<CachedProjectMetadataReferences> cached,
        IReadOnlyList<CachedProjectMetadataReferences> actual) => actual.All(project => project.References.All(reference =>
            cached.Where(candidate => FileSystemPath.Comparer.Equals(candidate.ProjectPath, project.ProjectPath))
                .SelectMany(candidate => candidate.References).Where(candidate => FileSystemPath.Comparer.Equals(candidate.AssemblyPath, reference.AssemblyPath))
                .All(candidate => candidate.LastWriteTimeUtc == reference.LastWriteTimeUtc)));
}

internal sealed record WorkspaceCacheProbe(string SolutionHash, string SdkVersion, WorkspaceCacheEntry? CachedEntry);
internal sealed record WorkspaceCacheWriteResult(bool CacheHit);
