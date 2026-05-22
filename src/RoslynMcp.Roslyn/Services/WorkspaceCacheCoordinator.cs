using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

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

    /// <summary>
    /// Probe-stage cache lookup performed before MSBuild opens the solution. Captures the
    /// (solutionHash, sdkVersion) prefix of the cache-key triple; the graph hash is validated
    /// after load by <see cref="ResolveAndWriteAsync"/>.
    /// </summary>
    public async Task<WorkspaceCacheProbe?> TryProbeAsync(string fullPath, CancellationToken ct)
    {
        try
        {
            var solutionHash = await ComputeSolutionContentHashAsync(fullPath, ct).ConfigureAwait(false);
            if (solutionHash is null) return null;

            var sdkVersion = ResolveSdkVersionForCacheKey();
            var candidate = await TryEnumerateNewestCacheEntryAsync(solutionHash, sdkVersion, ct).ConfigureAwait(false);
            if (candidate is null)
            {
                return new WorkspaceCacheProbe(
                    solutionHash, sdkVersion, CachedEntry: null, MetadataReferencesStillStable: false);
            }

            var stable = MetadataReferencesStillStableOnDisk(candidate);
            return new WorkspaceCacheProbe(solutionHash, sdkVersion, candidate, stable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Workspace cache probe failed for '{Path}'; cold-load path will run.", fullPath);
            return null;
        }
    }

    /// <summary>
    /// Post-load reconciliation. Confirms a probe-stage candidate against the actual graph and
    /// writes the current graph/metadata entry under the canonical graph key.
    /// </summary>
    public async Task<WorkspaceCacheWriteResult?> ResolveAndWriteAsync(
        Solution solution,
        WorkspaceCacheProbe probe,
        CancellationToken ct)
    {
        try
        {
            var actualGraph = BuildCachedGraphFromSolution(solution);
            var actualMetadataRefs = BuildCachedMetadataRefsFromSolution(solution);
            var actualGraphHash = ComputeMsbuildGraphHash(actualGraph);
            var canonicalKey = new WorkspaceCacheKey(probe.SolutionHash, probe.SdkVersion, actualGraphHash);
            var cacheHit = false;

            if (probe.CachedEntry is not null)
            {
                var cachedGraphHash = ComputeMsbuildGraphHash(probe.CachedEntry.ProjectGraph);
                if (string.Equals(cachedGraphHash, actualGraphHash, StringComparison.Ordinal)
                    && probe.MetadataReferencesStillStable)
                {
                    cacheHit = true;
                }
            }

            var entryToWrite = new WorkspaceCacheEntry(actualGraph, actualMetadataRefs, DateTime.UtcNow);
            _ = await _cacheStore.PutAsync(canonicalKey, entryToWrite, ct).ConfigureAwait(false);
            return new WorkspaceCacheWriteResult(cacheHit);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Workspace cache write failed for '{SolutionHash}'; cold path stands.", probe.SolutionHash);
            return null;
        }
    }

    /// <summary>
    /// Cold-path cache write used when the probe found no candidate for the solution/sdk pair.
    /// </summary>
    public async Task<WorkspaceCacheWriteResult?> WriteFreshEntryAsync(
        Solution solution,
        string fullPath,
        CancellationToken ct)
    {
        try
        {
            var solutionHash = await ComputeSolutionContentHashAsync(fullPath, ct).ConfigureAwait(false);
            if (solutionHash is null) return null;

            var sdkVersion = ResolveSdkVersionForCacheKey();
            var graph = BuildCachedGraphFromSolution(solution);
            var metadataRefs = BuildCachedMetadataRefsFromSolution(solution);
            var graphHash = ComputeMsbuildGraphHash(graph);

            var key = new WorkspaceCacheKey(solutionHash, sdkVersion, graphHash);
            var entry = new WorkspaceCacheEntry(graph, metadataRefs, DateTime.UtcNow);

            _ = await _cacheStore.PutAsync(key, entry, ct).ConfigureAwait(false);
            return new WorkspaceCacheWriteResult(CacheHit: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Workspace cache write failed for '{Path}'; cold path stands.", fullPath);
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

    internal static async Task<string?> ComputeSolutionContentHashAsync(string fullPath, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
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

    internal static IReadOnlyList<CachedProjectMetadataReferences> BuildCachedMetadataRefsFromSolution(Solution solution)
    {
        var result = new List<CachedProjectMetadataReferences>(solution.ProjectIds.Count);
        foreach (var project in solution.Projects.OrderBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(project.FilePath)) continue;
            var refs = new List<CachedMetadataReference>(project.MetadataReferences.Count);
            foreach (var mref in project.MetadataReferences.OfType<PortableExecutableReference>())
            {
                if (string.IsNullOrEmpty(mref.FilePath)) continue;
                DateTime mtime;
                try
                {
                    mtime = File.GetLastWriteTimeUtc(mref.FilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                    continue;
                }
                refs.Add(new CachedMetadataReference(mref.FilePath, mtime));
            }
            result.Add(new CachedProjectMetadataReferences(project.FilePath, refs));
        }
        return result;
    }

    internal static bool MetadataReferencesStillStableOnDisk(WorkspaceCacheEntry entry)
    {
        foreach (var projectRefs in entry.MetadataReferences)
        {
            foreach (var mref in projectRefs.References)
            {
                try
                {
                    if (!File.Exists(mref.AssemblyPath)) return false;
                    var mtime = File.GetLastWriteTimeUtc(mref.AssemblyPath);
                    if (mtime != mref.LastWriteTimeUtc) return false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                    return false;
                }
            }
        }
        return true;
    }
}

internal sealed record WorkspaceCacheProbe(
    string SolutionHash,
    string SdkVersion,
    WorkspaceCacheEntry? CachedEntry,
    bool MetadataReferencesStillStable);

internal sealed record WorkspaceCacheWriteResult(bool CacheHit);
