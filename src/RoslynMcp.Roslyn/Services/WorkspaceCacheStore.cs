using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// On-disk JSON implementation of <see cref="IWorkspaceCacheStore"/>. Persists cache
/// entries under <c>~/.roslyn-mcp/cache/&lt;solution-hash&gt;/&lt;sdk-version&gt;/&lt;msbuild-graph-hash&gt;/entry.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// On-disk format is version-tagged (<see cref="CurrentFormatVersion"/>). A read encountering
/// a different format version is treated as a miss; this provides a clean upgrade path for
/// future schema changes without bespoke migration code.
/// </para>
///
/// <para>
/// All write paths use the temp-file + <see cref="File.Move(string, string, bool)"/> pattern
/// for atomicity — a partially written entry never poisons subsequent reads. All
/// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>,
/// <see cref="JsonException"/> and similar fail-soft conditions are swallowed: best-effort
/// is the contract from the interface.
/// </para>
///
/// <para>
/// Cache-key components are hashed before being used as path segments to prevent path-style
/// mismatches between Windows / Linux / macOS from poisoning sibling caches. Raw paths from
/// the workspace evaluator never appear in the directory tree — only their content hashes.
/// </para>
///
/// <para>
/// This service does not enforce a global size cap on the cache directory. Cleanup is the
/// responsibility of consumer-side housekeeping (a future row); each individual entry is
/// small (a few KB per project) so the unbounded-growth risk is bounded by
/// solution-count × sdk-version-count × graph-mutation-count, all of which are small in
/// practice.
/// </para>
/// </remarks>
public sealed class WorkspaceCacheStore : IWorkspaceCacheStore
{
    /// <summary>
    /// Bumped whenever the on-disk format changes shape. Reads encountering a different
    /// version are treated as a miss (clean invalidation, no migration code path).
    /// </summary>
    internal const int CurrentFormatVersion = 1;

    private const string EntryFileName = "entry.json";
    private const string TempFileName = "entry.json.tmp";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _rootDirectory;
    private readonly ILogger<WorkspaceCacheStore>? _logger;

    /// <summary>
    /// Creates a store rooted at <c>~/.roslyn-mcp/cache/</c> (resolved via
    /// <see cref="Environment.SpecialFolder.UserProfile"/>). Mkdir is idempotent and
    /// fail-soft — if the user-profile path is not writable, the store still constructs
    /// but every read/write becomes a no-op miss.
    /// </summary>
    public WorkspaceCacheStore(ILogger<WorkspaceCacheStore>? logger = null)
        : this(ResolveDefaultRootDirectory(), logger)
    {
    }

    /// <summary>
    /// Creates a store rooted at an explicit directory — used by tests to redirect into a
    /// per-test temp tree.
    /// </summary>
    /// <param name="rootDirectory">Absolute path to the cache root.</param>
    /// <param name="logger">Optional logger; <see langword="null"/> in test/CLI fixtures.</param>
    public WorkspaceCacheStore(string rootDirectory, ILogger<WorkspaceCacheStore>? logger = null)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        _logger = logger;

        TryEnsureDirectoryExists(_rootDirectory);
    }

    /// <inheritdoc />
    public Task<WorkspaceCacheEntry?> TryGetAsync(WorkspaceCacheKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        var entryPath = ResolveEntryPath(key);
        return TryGetEntryAtPathAsync(entryPath, ct);
    }

    /// <summary>
    /// Reads a concrete <c>entry.json</c> path discovered by cache-root enumeration.
    /// This intentionally bypasses cache-key hashing because the directory leaf is already
    /// the hashed graph segment and the original graph hash cannot be recovered from it.
    /// </summary>
    internal Task<WorkspaceCacheEntry?> TryGetEntryAtPathAsync(string entryPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(entryPath))
        {
            return Task.FromResult<WorkspaceCacheEntry?>(null);
        }

        try
        {
            var json = File.ReadAllText(entryPath);
            var dto = JsonSerializer.Deserialize<PersistedEntryV1>(json, JsonOpts);
            if (dto is null || dto.FormatVersion != CurrentFormatVersion)
            {
                // Format mismatch is a clean miss — drop the entry so the next put
                // doesn't have to fight a stale version.
                TryDeleteFile(entryPath);
                return Task.FromResult<WorkspaceCacheEntry?>(null);
            }

            var entry = new WorkspaceCacheEntry(
                dto.ProjectGraph
                    .Select(p => new CachedProjectGraphNode(p.ProjectPath, p.ProjectReferences))
                    .ToArray(),
                dto.MetadataReferences
                    .Select(m => new CachedProjectMetadataReferences(
                        m.ProjectPath,
                        m.References
                            .Select(r => new CachedMetadataReference(r.AssemblyPath, r.LastWriteTimeUtc))
                            .ToArray()))
                    .ToArray(),
                dto.CapturedAtUtc);

            return Task.FromResult<WorkspaceCacheEntry?>(entry);
        }
        catch (Exception ex) when (IsExpectedReadFailure(ex))
        {
            // Corrupt or unreadable entry — drop it and return null. The next put will
            // cleanly overwrite. Log at debug so operators can investigate without the
            // log being spammed in normal operation.
            _logger?.LogDebug(ex, "WorkspaceCacheStore: dropping unreadable entry at {Path}.", entryPath);
            TryDeleteFile(entryPath);
            return Task.FromResult<WorkspaceCacheEntry?>(null);
        }
    }

    /// <inheritdoc />
    public Task<bool> PutAsync(WorkspaceCacheKey key, WorkspaceCacheEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        var entryPath = ResolveEntryPath(key);
        var entryDir = Path.GetDirectoryName(entryPath);
        if (entryDir is null || !TryEnsureDirectoryExists(entryDir))
        {
            return Task.FromResult(false);
        }

        var tmpPath = Path.Combine(entryDir, TempFileName);

        try
        {
            var dto = new PersistedEntryV1(
                CurrentFormatVersion,
                entry.ProjectGraph
                    .Select(n => new PersistedGraphNode(n.ProjectPath, n.ProjectReferences.ToArray()))
                    .ToArray(),
                entry.MetadataReferences
                    .Select(m => new PersistedMetadataRefList(
                        m.ProjectPath,
                        m.References
                            .Select(r => new PersistedMetadataRef(r.AssemblyPath, r.LastWriteTimeUtc))
                            .ToArray()))
                    .ToArray(),
                entry.CapturedAtUtc);

            File.WriteAllText(tmpPath, JsonSerializer.Serialize(dto, JsonOpts));
            File.Move(tmpPath, entryPath, overwrite: true);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (IsExpectedWriteFailure(ex))
        {
            _logger?.LogDebug(ex, "WorkspaceCacheStore: write to {Path} failed; treating as cache miss next time.", entryPath);
            TryDeleteFile(tmpPath);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task InvalidateAsync(WorkspaceCacheKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        var entryPath = ResolveEntryPath(key);
        if (File.Exists(entryPath))
        {
            TryDeleteFile(entryPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the on-disk path for a cache entry. Path-segment safety is provided by hashing
    /// every key component before use — prevents an evaluator-supplied SDK string with
    /// characters like <c>:</c> or <c>/</c> from creating a sibling directory.
    /// </summary>
    internal string ResolveEntryPath(WorkspaceCacheKey key)
    {
        var solutionSegment = HashForPath(key.SolutionHash);
        var sdkSegment = HashForPath(key.SdkVersion);
        var graphSegment = HashForPath(key.MsbuildGraphHash);

        return Path.Combine(_rootDirectory, solutionSegment, sdkSegment, graphSegment, EntryFileName);
    }

    /// <summary>
    /// Default cache root: <c>~/.roslyn-mcp/cache/</c>. Resolved via
    /// <see cref="Environment.SpecialFolder.UserProfile"/> so the same expression works on
    /// Windows (<c>%USERPROFILE%</c>), Linux/macOS (<c>$HOME</c>), and CI containers where
    /// either may be set.
    /// </summary>
    public static string ResolveDefaultRootDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            // Last-ditch fallback: temp directory. Cache becomes per-process here, but
            // that's acceptable degradation when no user-profile is resolvable.
            home = Path.GetTempPath();
        }
        return Path.Combine(home, ".roslyn-mcp", "cache");
    }

    /// <summary>
    /// Hashes a key component using SHA-256 truncated to 16 hex chars. Stable across processes
    /// and platforms; collision-resistant for the cache-key cardinality this store sees in
    /// practice (one solution * SDK count * graph mutations).
    /// </summary>
    private static string HashForPath(string component)
    {
        if (string.IsNullOrEmpty(component))
        {
            return "empty";
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(component);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);

        // Truncate to 16 hex chars (64 bits) — gives ~10^9 collision-resistance for
        // cache-key cardinality, plenty for this domain.
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private bool TryEnsureDirectoryExists(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (IsExpectedWriteFailure(ex))
        {
            _logger?.LogDebug(ex, "WorkspaceCacheStore: failed to ensure directory {Path}.", path);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup — a stale file will simply fail to deserialize on the
            // next read and be cleaned up there.
        }
    }

    private static bool IsExpectedReadFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or JsonException
        or System.Text.DecoderFallbackException;

    private static bool IsExpectedWriteFailure(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException;

    /// <summary>
    /// On-disk shape v1. Property names are stable wire format; do not rename.
    /// </summary>
    internal sealed record PersistedEntryV1(
        [property: JsonPropertyName("formatVersion")] int FormatVersion,
        [property: JsonPropertyName("projectGraph")] IReadOnlyList<PersistedGraphNode> ProjectGraph,
        [property: JsonPropertyName("metadataReferences")] IReadOnlyList<PersistedMetadataRefList> MetadataReferences,
        [property: JsonPropertyName("capturedAtUtc")] DateTime CapturedAtUtc);

    internal sealed record PersistedGraphNode(
        [property: JsonPropertyName("projectPath")] string ProjectPath,
        [property: JsonPropertyName("projectReferences")] IReadOnlyList<string> ProjectReferences);

    internal sealed record PersistedMetadataRefList(
        [property: JsonPropertyName("projectPath")] string ProjectPath,
        [property: JsonPropertyName("references")] IReadOnlyList<PersistedMetadataRef> References);

    internal sealed record PersistedMetadataRef(
        [property: JsonPropertyName("assemblyPath")] string AssemblyPath,
        [property: JsonPropertyName("lastWriteTimeUtc")] DateTime LastWriteTimeUtc);
}
