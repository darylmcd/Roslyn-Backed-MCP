using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 6 (v1.18): on-disk store for <see cref="CompositePreviewStore.Entry"/> records. Lets a
/// preview created in one <c>roslynmcp</c> process be redeemed by another (multi-agent
/// orchestration). Activated when <see cref="PreviewStoreOptions.PersistDirectory"/> is set
/// (typically via <c>ROSLYNMCP_PREVIEW_PERSIST_DIR</c>).
/// </summary>
/// <remarks>
/// Layout: <c>{root}/{workspaceVersion}/{token}.json</c>. Atomic writes via
/// <c>{token}.json.tmp</c> + <c>File.Move</c>. TTL enforced at retrieve-time via the file's
/// last-write timestamp. Redemption atomically creates a per-token lock before renaming the
/// payload to a claim file, so only one host can receive a one-time token.
/// </remarks>
public sealed class PersistentCompositeStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDirectory;
    private readonly TimeSpan _ttl;
    private readonly ILogger<PersistentCompositeStorage>? _logger;
    private readonly Func<string, IEnumerable<string>> _enumerateDirectoriesForRead;
    private readonly Func<string, DateTime> _getLastWriteTimeUtc;
    private readonly Action<string> _deleteFile;
    private readonly Func<string, FileStream> _createClaimLock;

    public PersistentCompositeStorage(
        string rootDirectory,
        TimeSpan ttl,
        ILogger<PersistentCompositeStorage>? logger = null)
        : this(
            rootDirectory,
            ttl,
            logger,
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            File.Delete,
            CreateClaimLock)
    {
    }

    internal PersistentCompositeStorage(
        string rootDirectory,
        TimeSpan ttl,
        ILogger<PersistentCompositeStorage>? logger,
        Func<string, IEnumerable<string>> enumerateDirectoriesForRead,
        Func<string, DateTime> getLastWriteTimeUtc,
        Action<string> deleteFile,
        Func<string, FileStream> createClaimLock)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);
        _logger = logger;
        _enumerateDirectoriesForRead = enumerateDirectoriesForRead
            ?? throw new ArgumentNullException(nameof(enumerateDirectoriesForRead));
        _getLastWriteTimeUtc = getLastWriteTimeUtc
            ?? throw new ArgumentNullException(nameof(getLastWriteTimeUtc));
        _deleteFile = deleteFile ?? throw new ArgumentNullException(nameof(deleteFile));
        _createClaimLock = createClaimLock ?? throw new ArgumentNullException(nameof(createClaimLock));
        Directory.CreateDirectory(_rootDirectory);
        CleanupExpiredClaimArtifacts();
    }

    public void Write(string token, CompositePreviewStore.Entry entry)
    {
        ValidateToken(token);

        var dir = Path.Combine(_rootDirectory, entry.WorkspaceVersion.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, token + ".json");
        var tmp = path + ".tmp";

        var dto = new PersistedEntry(
            entry.WorkspaceId,
            entry.WorkspaceVersion,
            entry.Description,
            entry.Mutations.Select(m => new PersistedMutation(m.FilePath, m.UpdatedContent, m.DeleteFile)).ToArray(),
            entry.CreatedAt);

        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            // A failed write/move must not leave an orphaned payload that is never eligible
            // for normal token retrieval or TTL cleanup. TryDelete is best-effort and preserves
            // the primary write exception.
            if (File.Exists(tmp))
            {
                TryDelete(tmp);
            }
        }
    }

    public CompositePreviewStore.Entry? TryClaim(string token)
    {
        if (!IsValidToken(token))
        {
            return null;
        }

        // Search across workspaceVersion subdirectories — caller doesn't know the version
        // when redeeming a token from a separate process.
        var versionDirectories = TryEnumerateVersionDirectories("locating token");
        if (versionDirectories is null) return null;

        foreach (var subdir in versionDirectories)
        {
            var path = Path.Combine(subdir, token + ".json");
            if (!File.Exists(path))
            {
                continue;
            }

            return ClaimEntry(path);
        }

        return null;
    }

    /// <summary>
    /// Compatibility entry point for callers compiled against the original storage API.
    /// Reads now have the same one-time, fail-closed semantics as <see cref="TryClaim"/>.
    /// </summary>
    public CompositePreviewStore.Entry? TryRead(string token) => TryClaim(token);

    public void Delete(string token)
    {
        if (!IsValidToken(token))
        {
            return;
        }

        var versionDirectories = TryEnumerateVersionDirectories("deleting token");
        if (versionDirectories is null) return;

        foreach (var subdir in versionDirectories)
        {
            var path = Path.Combine(subdir, token + ".json");
            _deleteFile(path);
        }
    }

    private CompositePreviewStore.Entry? ClaimEntry(string path)
    {
        var lockPath = path + ".lock";
        FileStream claimLock;
        try
        {
            claimLock = _createClaimLock(lockPath);
        }
        catch (IOException ex) when (File.Exists(lockPath) || !File.Exists(path))
        {
            _logger?.LogDebug(ex, "PersistentCompositeStorage: token was claimed by another process.");
            return null;
        }

        try
        {
            var claimPath = path + ".claim";
            try
            {
                File.Move(path, claimPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _logger?.LogDebug(ex, "PersistentCompositeStorage: token payload disappeared before claim.");
                return null;
            }

            try
            {
                return ReadClaimedEntry(claimPath);
            }
            finally
            {
                // Delete before returning the payload. A deletion failure is visible and prevents
                // mutation from starting with a token whose one-time record still exists.
                DeleteClaimArtifact(claimPath);
            }
        }
        finally
        {
            claimLock.Dispose();
            DeleteClaimArtifact(lockPath);
        }
    }

    private CompositePreviewStore.Entry? ReadClaimedEntry(string claimPath)
    {
        // Read first so another process cannot slip a deletion between a path-based timestamp
        // probe and opening the claim. Claim-file I/O remains fail-closed and visible.
        var json = File.ReadAllText(claimPath);
        if (DateTime.UtcNow - _getLastWriteTimeUtc(claimPath) > _ttl)
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<PersistedEntry>(json, JsonOpts);
            return dto is null
                ? null
                : new CompositePreviewStore.Entry(
                    dto.WorkspaceId,
                    dto.WorkspaceVersion,
                    dto.Description,
                    dto.Mutations.Select(m => new CompositeFileMutation(m.FilePath, m.UpdatedContent, m.DeleteFile)).ToArray(),
                    dto.CreatedAt);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger?.LogDebug(ex, "PersistentCompositeStorage: dropping unreadable claimed entry.");
            return null;
        }
    }

    private string[]? TryEnumerateVersionDirectories(string operation)
    {
        if (!Directory.Exists(_rootDirectory)) return null;

        try
        {
            // Materialize inside this narrow catch because Directory.EnumerateDirectories is lazy.
            // A sibling process removing the root during MoveNext is an idempotent cache miss.
            return _enumerateDirectoriesForRead(_rootDirectory).ToArray();
        }
        catch (IOException ex)
        {
            _logger?.LogDebug(
                ex,
                "PersistentCompositeStorage: directory race while {Operation}; treating as miss.",
                operation);
            return null;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            _deleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup for a write's temporary sibling. The primary write failure
            // remains authoritative; no redeemable token payload exists at this path.
            _logger?.LogDebug(ex, "PersistentCompositeStorage: failed to delete entry at {Path}.", path);
        }
    }

    private void DeleteClaimArtifact(string path)
    {
        const int maximumAttempts = 3;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                _deleteFile(path);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                // A losing claimant may briefly hold a metadata handle while observing the
                // shared claim. Retry only the transient I/O shape; permission failures and a
                // terminal I/O failure remain visible before mutation starts.
                Thread.Sleep(attempt * 10);
            }
        }
    }

    private static FileStream CreateClaimLock(string lockPath) =>
        new(
            lockPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);

    private void CleanupExpiredClaimArtifacts()
    {
        var now = DateTime.UtcNow;
        foreach (var versionDirectory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var claimArtifacts = Directory
                .EnumerateFiles(versionDirectory, "*.json.claim")
                .Concat(Directory.EnumerateFiles(versionDirectory, "*.json.lock"));
            foreach (var claimPath in claimArtifacts)
            {
                if (now - _getLastWriteTimeUtc(claimPath) > _ttl)
                {
                    DeleteClaimArtifact(claimPath);
                }
            }
        }
    }

    private static bool IsValidToken(string token)
        => Guid.TryParseExact(token, "N", out _);

    private static void ValidateToken(string token)
    {
        if (!IsValidToken(token))
        {
            throw new ArgumentException(
                "Persistent preview tokens must be 32 hexadecimal GUID characters.",
                nameof(token));
        }
    }

    private sealed record PersistedEntry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<PersistedMutation> Mutations,
        DateTime CreatedAt);

    private sealed record PersistedMutation(string FilePath, string? UpdatedContent, bool DeleteFile);
}
