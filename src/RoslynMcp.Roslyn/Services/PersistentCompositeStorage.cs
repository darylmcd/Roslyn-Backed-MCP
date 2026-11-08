using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

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

    /// <summary>
    /// Record format written by this server. 1 (no <c>FormatVersion</c> member) bound a record to the
    /// creating session id; 2 adds <c>WorkspaceIdentity</c>, the normalized solution/project path;
    /// 3 adds <c>SourceFingerprints</c> (ADR 0033). Format 1 and 2 records still read, with no
    /// fingerprints, and are redeemed unchecked. A reader refuses a newer format, so a reader at
    /// format 2 refuses a format 3 record: the change is additive only for readers at 3 or later.
    /// </summary>
    private const int CurrentFormatVersion = 3;

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

        // WorkspaceId and WorkspaceVersion are diagnostics only from format 2 on: the id names a
        // session of the creating process and the version counts that session's reloads, so neither
        // identifies anything on another host. WorkspaceIdentity is the portable key.
        var identity = !string.IsNullOrWhiteSpace(entry.WorkspaceIdentity)
            ? entry.WorkspaceIdentity
            : throw new PublicArgumentException(
                "A persisted composite entry requires a workspace identity.", nameof(entry));
        var fingerprints = entry.SourceFingerprints
            ?? throw new PublicArgumentException(
                "A persisted composite entry requires source fingerprints.", nameof(entry));
        var dto = new PersistedEntry(
            entry.WorkspaceId,
            entry.WorkspaceVersion,
            entry.Description,
            entry.Mutations.Select(m => new PersistedMutation(m.FilePath, m.UpdatedContent, m.DeleteFile)).ToArray(),
            entry.CreatedAt,
            CurrentFormatVersion,
            identity,
            fingerprints.Select(f => new PersistedFingerprint(f.FilePath, f.Exists, f.Sha256)).ToArray());

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

    /// <summary>
    /// Claims the one-time record for <paramref name="token"/>. <paramref name="admit"/> runs on the
    /// parsed record while this process holds the claim lock and BEFORE the payload is moved: it maps
    /// the record onto a workspace session of this host and returns that session's id (which replaces
    /// the record's <c>WorkspaceId</c> in the result). If it throws, the payload stays on disk, the lock
    /// is released and the exception propagates, so a refusal never consumes the token. A record from a
    /// newer format is refused the same way (<see langword="null"/>, payload kept). A corrupt or
    /// expired record is consumed and dropped.
    /// </summary>
    public CompositePreviewStore.Entry? TryClaim(string token, Func<CompositePreviewStore.Entry, string> admit)
    {
        ArgumentNullException.ThrowIfNull(admit);
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

            return ClaimEntry(path, admit);
        }

        return null;
    }

    /// <summary>
    /// Reads the persisted entry for <paramref name="token"/> WITHOUT claiming it, so a caller can
    /// inspect what <see cref="TryClaim"/> would hand out (for example to revalidate its write set)
    /// while the one-time token stays redeemable. Returns <see langword="null"/> for every shape in
    /// which a claim would not succeed: invalid, absent, expired, mid-claim (lock sibling present),
    /// vanished underneath the read, or unreadable/incomplete JSON.
    /// </summary>
    public CompositePreviewStore.Entry? TryPeek(string token)
    {
        if (!IsValidToken(token))
        {
            return null;
        }

        var versionDirectories = TryEnumerateVersionDirectories("peeking token");
        if (versionDirectories is null) return null;

        foreach (var subdir in versionDirectories)
        {
            var path = Path.Combine(subdir, token + ".json");
            if (!File.Exists(path))
            {
                continue;
            }

            return PeekEntry(path);
        }

        return null;
    }

    private CompositePreviewStore.Entry? PeekEntry(string path)
    {
        // A lock sibling means a claimant is mid-claim: the payload is about to be consumed.
        if (File.Exists(path + ".lock"))
        {
            return null;
        }

        string json;
        DateTime lastWriteUtc;
        try
        {
            (json, lastWriteUtc) = ReadPayload(path);
        }
        catch (IOException ex)
        {
            // Claimed or removed between the existence probe and the open: indistinguishable from a
            // cache miss, and a miss makes the caller refuse. Permission failures still propagate.
            _logger?.LogDebug(ex, "PersistentCompositeStorage: token payload disappeared before peek.");
            return null;
        }

        // A record from a newer format cannot be interpreted safely (its identity rules are unknown),
        // so its write set is "unknown" and the caller fails closed.
        return DateTime.UtcNow - lastWriteUtc > _ttl
            ? null
            : ParseEntry(json, out var entry) == ParseOutcome.Parsed ? entry : null;
    }

    /// <summary>
    /// Reads the payload and its timestamp. FileShare.Delete is load-bearing: <see cref="ClaimEntry"/>
    /// renames this very file with File.Move, and a reader holding it open without Delete sharing makes
    /// the winning claimant's move throw a raw IOException on Windows. The timestamp is probed while the
    /// handle is still held so a concurrent claim (rename) is observed as an expired/absent entry, never
    /// a torn read. The handle is closed before the caller acts on the content.
    /// </summary>
    private (string Json, DateTime LastWriteUtc) ReadPayload(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return (json, _getLastWriteTimeUtc(path));
    }

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

    private CompositePreviewStore.Entry? ClaimEntry(string path, Func<CompositePreviewStore.Entry, string> admit)
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
            // Read, parse and admit BEFORE the payload is moved. A refusal (newer format, or admit
            // throwing because this host has no matching session) leaves the one-time payload on disk
            // for a correctly prepared host; the lock is released by the finally below.
            string json;
            DateTime lastWriteUtc;
            try
            {
                (json, lastWriteUtc) = ReadPayload(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _logger?.LogDebug(ex, "PersistentCompositeStorage: token payload disappeared before claim.");
                return null;
            }

            CompositePreviewStore.Entry? admitted = null;
            if (DateTime.UtcNow - lastWriteUtc <= _ttl)
            {
                switch (ParseEntry(json, out var parsed))
                {
                    case ParseOutcome.NewerFormat:
                        return null;
                    case ParseOutcome.Parsed:
                        // Exceptions propagate on purpose: they are the refusal, and the payload is untouched.
                        admitted = parsed! with { WorkspaceId = admit(parsed!) };
                        break;
                }
            }

            // Expired and unreadable records are consumed and dropped, as before.
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

            // Delete before returning the record. A deletion failure is visible and prevents
            // mutation from starting with a token whose one-time record still exists. The returned
            // record is the one that was admitted, never a re-read of a file that may have been swapped.
            DeleteClaimArtifact(claimPath);
            return admitted;
        }
        finally
        {
            claimLock.Dispose();
            DeleteClaimArtifact(lockPath);
        }
    }

    private enum ParseOutcome
    {
        Parsed,
        Unreadable,
        NewerFormat,
    }

    /// <summary>
    /// Parses a persisted record. The directory is shared and separately writable, so the payload is
    /// untrusted input: a record missing the members every later stage dereferences (workspace id,
    /// mutation list, a mutation's path, for format 2 the workspace identity, for format 3 the fingerprint list) is unreadable,
    /// never a half-populated entry. A record without a <c>FormatVersion</c> is format 1; one above
    /// <see cref="CurrentFormatVersion"/> is reported separately because its payload must be kept.
    /// </summary>
    private ParseOutcome ParseEntry(string json, out CompositePreviewStore.Entry? entry)
    {
        entry = null;
        try
        {
            var dto = JsonSerializer.Deserialize<PersistedEntry>(json, JsonOpts);
            if (dto is null)
            {
                _logger?.LogDebug("PersistentCompositeStorage: dropping persisted entry with missing required members.");
                return ParseOutcome.Unreadable;
            }

            if (dto.FormatVersion > CurrentFormatVersion)
            {
                _logger?.LogDebug(
                    "PersistentCompositeStorage: persisted entry has format {FormatVersion}, newer than supported {Supported}; keeping it.",
                    dto.FormatVersion,
                    CurrentFormatVersion);
                return ParseOutcome.NewerFormat;
            }

            if (string.IsNullOrWhiteSpace(dto.WorkspaceId) || dto.Mutations is null)
            {
                _logger?.LogDebug("PersistentCompositeStorage: dropping persisted entry with missing required members.");
                return ParseOutcome.Unreadable;
            }

            string? identity = null;
            if (dto.FormatVersion >= 2)
            {
                // A relative identity would be resolved against this process's working directory by
                // GetFullPath and could match an unrelated session: it is unusable, not normalizable.
                if (string.IsNullOrWhiteSpace(dto.WorkspaceIdentity) || !Path.IsPathFullyQualified(dto.WorkspaceIdentity))
                {
                    _logger?.LogDebug("PersistentCompositeStorage: dropping format-2 entry without a fully qualified workspace identity.");
                    return ParseOutcome.Unreadable;
                }

                identity = Path.GetFullPath(dto.WorkspaceIdentity);
            }

            var mutations = new List<CompositeFileMutation>(dto.Mutations.Count);
            foreach (var mutation in dto.Mutations)
            {
                if (mutation is null || string.IsNullOrWhiteSpace(mutation.FilePath))
                {
                    _logger?.LogDebug("PersistentCompositeStorage: dropping persisted entry with a pathless mutation.");
                    return ParseOutcome.Unreadable;
                }

                mutations.Add(new CompositeFileMutation(mutation.FilePath, mutation.UpdatedContent, mutation.DeleteFile));
            }

            // Format 1 and 2 records predate fingerprints: null means "redeem unchecked". From format 3 the
            // list is required, so deleting it from a record cannot silently turn the check off.
            List<CompositeSourceFingerprint>? fingerprints = null;
            if (dto.FormatVersion >= 3)
            {
                if (dto.SourceFingerprints is null)
                {
                    _logger?.LogDebug("PersistentCompositeStorage: dropping format-3 entry without source fingerprints.");
                    return ParseOutcome.Unreadable;
                }

                // The redeeming host reads, hashes and reports on every fingerprint path, so a path that
                // no mutation writes (an arbitrary file chosen by whoever can write this directory) is
                // refused: fingerprints are a subset of the write set.
                var writeSet = new HashSet<string>(FileSystemPath.Comparer);
                foreach (var mutation in mutations)
                {
                    writeSet.Add(Path.GetFullPath(mutation.FilePath));
                }

                fingerprints = new List<CompositeSourceFingerprint>(dto.SourceFingerprints.Count);
                foreach (var fingerprint in dto.SourceFingerprints)
                {
                    if (fingerprint is null
                        || string.IsNullOrWhiteSpace(fingerprint.FilePath)
                        || (fingerprint.Exists && string.IsNullOrWhiteSpace(fingerprint.Sha256)))
                    {
                        _logger?.LogDebug("PersistentCompositeStorage: dropping persisted entry with a malformed source fingerprint.");
                        return ParseOutcome.Unreadable;
                    }

                    if (!writeSet.Contains(Path.GetFullPath(fingerprint.FilePath)))
                    {
                        _logger?.LogDebug("PersistentCompositeStorage: dropping persisted entry with a source fingerprint outside its write set.");
                        return ParseOutcome.Unreadable;
                    }

                    fingerprints.Add(new CompositeSourceFingerprint(
                        fingerprint.FilePath,
                        fingerprint.Exists,
                        fingerprint.Exists ? fingerprint.Sha256 : null));
                }
            }

            entry = new CompositePreviewStore.Entry(
                dto.WorkspaceId,
                dto.WorkspaceVersion,
                dto.Description,
                mutations,
                dto.CreatedAt,
                identity,
                fingerprints);
            return ParseOutcome.Parsed;
        }
        // IOException: Path.GetFullPath on an untrusted overlong path throws PathTooLongException.
        // Nothing else in this body performs I/O, so the record is simply unreadable.
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or IOException)
        {
            _logger?.LogDebug(ex, "PersistentCompositeStorage: dropping unreadable persisted entry.");
            return ParseOutcome.Unreadable;
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
            throw new PublicArgumentException(
                "Persistent preview tokens must be 32 hexadecimal GUID characters.",
                nameof(token));
        }
    }

    // Members are nullable because the record is deserialized from a shared directory: JSON can omit
    // or null any of them, and ParseEntry rejects such a record instead of dereferencing it.
    // FormatVersion is absent (0) in format-1 records. From format 2, WorkspaceId and WorkspaceVersion
    // are diagnostics only; WorkspaceIdentity is the portable key (ADR 0032). SourceFingerprints is
    // absent before format 3 (ADR 0033).
    private sealed record PersistedEntry(
        string? WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<PersistedMutation?>? Mutations,
        DateTime CreatedAt,
        int FormatVersion,
        string? WorkspaceIdentity,
        IReadOnlyList<PersistedFingerprint?>? SourceFingerprints);

    private sealed record PersistedMutation(string? FilePath, string? UpdatedContent, bool DeleteFile);

    private sealed record PersistedFingerprint(string? FilePath, bool Exists, string? Sha256);
}
