using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// <c>host-recycle-opacity</c>: lightweight disk-backed lifecycle store that lets a freshly-started
/// host stdio process surface metadata about the PRIOR process — pid, exit time, recycle reason —
/// on its first <c>server_info</c> / <c>server_heartbeat</c> probe. Pre-fix, when the host was
/// recycled mid-session (idle eviction, watchdog kill, client disconnect, crash), every
/// subsequent workspace-scoped tool call returned <c>NotFound</c> with no recycle reason —
/// the agent had to guess "did the process die?" by elimination.
/// <para>
/// The store has two halves:
/// </para>
/// <list type="number">
///   <item><description><see cref="LoadPrevious"/> — called once at host startup. Reads
///     the on-disk record (if any), validates it is fresh enough (<see cref="StaleAfter"/>),
///     and returns a snapshot. Subsequent calls return <see langword="null"/> — the snapshot
///     is consume-once so a second probe never re-emits previous-* fields.</description></item>
///   <item><description><see cref="WriteCurrent"/> — called on graceful shutdown via the
///     <c>ApplicationStopping</c> hook. Persists the current PID, exit timestamp, and recycle
///     reason so the NEXT process can read them.</description></item>
/// </list>
/// <para>
/// <strong>Persistence path:</strong> Windows uses <c>%LOCALAPPDATA%\roslyn-mcp\host-process.json</c>
/// (per-user, survives reboot, not synced to OneDrive when LocalAppData is at the default location).
/// Linux / macOS fall back to <c>$XDG_STATE_HOME/roslyn-mcp/host-process.json</c> with
/// <c>~/.local/state/roslyn-mcp/host-process.json</c> as the XDG default. Tests can override via
/// constructor injection.
/// </para>
/// <para>
/// <strong>TTL:</strong> a record older than 24 hours is treated as stale. We still surface
/// <c>previousStdioPid</c> / <c>previousExitedAt</c> if those parsed cleanly, but the recycle
/// reason is normalized to <c>unknown</c> and the timestamp is preserved verbatim — operators
/// always get a signal even if the prior session was paused for a long time. A record that
/// fails to parse at all is silently discarded; the file is best-effort, not load-bearing.
/// </para>
/// <para>
/// <strong>Crash safety:</strong> the file is written via temp-file + atomic-replace so a crash
/// during the write produces either the prior content or the new content, never a half-file.
/// We guard against stale writes (TTL) on read rather than on shutdown — a host that crashed
/// mid-write and left no file is indistinguishable from a cold start, and that is the
/// correct behavior: emit nothing.
/// </para>
/// </summary>
public sealed class HostProcessMetadataStore
{
    /// <summary>
    /// Records older than this are surfaced with reason <c>unknown</c> rather than the persisted
    /// reason. 24 hours is the longest plausible "the user paused work for the night and came
    /// back" window — anything beyond that is almost certainly a stale record from a long-dead
    /// process whose recycle context is no longer interesting.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private const string FileName = "host-process.json";
    private const string DirectoryName = "roslyn-mcp";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly ILogger? _logger;
    private readonly TimeSpan _staleAfter;
    private readonly Func<DateTime> _utcNow;
    private HostProcessMetadataSnapshot? _previous;
    private bool _previousLoaded;

    /// <summary>
    /// Production constructor — uses the default OS-specific path and the system clock.
    /// </summary>
    public HostProcessMetadataStore(ILogger? logger = null)
        : this(ResolveDefaultPath(), StaleAfter, () => DateTime.UtcNow, logger)
    {
    }

    /// <summary>
    /// Test constructor — injects a custom path, stale-after window, and clock.
    /// </summary>
    public HostProcessMetadataStore(string filePath, TimeSpan staleAfter, Func<DateTime> utcNow, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _staleAfter = staleAfter;
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _logger = logger;
    }

    /// <summary>
    /// Path to the persisted host-process metadata file. Exposed for diagnostics and tests;
    /// not part of any tool's wire contract.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Reads and CACHES the previous-process snapshot at first call. Subsequent calls return
    /// the same cached value. Consume-once semantics live in
    /// <see cref="HostProcessMetadataSnapshotProvider"/>; this method is idempotent so callers
    /// can inspect or re-publish without burning the latch.
    /// <para>
    /// Side-effect: deletes the on-disk record after a successful read so the same snapshot
    /// is not surfaced twice across separate process lifetimes (e.g. if the next host crashes
    /// before it gets a chance to write its own metadata).
    /// </para>
    /// </summary>
    public HostProcessMetadataSnapshot? LoadPrevious()
    {
        EnsureLoaded();
        return _previous;
    }

    /// <summary>
    /// Persists the current host-process metadata to disk. Best-effort: any IO failure is
    /// logged at <see cref="LogLevel.Warning"/> and swallowed — losing the next probe's
    /// previous-* fields is far less bad than crashing on shutdown because LocalAppData
    /// is read-only or full.
    /// </summary>
    /// <param name="recycleReason">Why the current process is exiting. <c>graceful</c> for
    /// the <c>ApplicationStopping</c> path; other values reserved for future watchdog /
    /// idle-eviction code that knows specifically why it terminated.</param>
    public void WriteCurrent(string recycleReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recycleReason);

        try
        {
            var record = new HostProcessMetadataRecord(
                StdioPid: Environment.ProcessId,
                ExitedAtUtc: _utcNow().ToString("O", CultureInfo.InvariantCulture),
                RecycleReason: recycleReason);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Temp-file + atomic-replace: a crash mid-write leaves either the old file or the
            // new one, never a half-written file that fails to parse.
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(record, s_jsonOptions));
            // File.Move with overwrite is atomic on NTFS and ext4. On macOS APFS it is also
            // atomic per-inode. Best-effort fallback: if Move fails, copy + delete (also OK
            // here because a partial state simply means the next process sees no record).
            try
            {
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (IOException)
            {
                File.Copy(tempPath, _filePath, overwrite: true);
                try { File.Delete(tempPath); } catch { /* leave the .tmp; not load-bearing */ }
            }
        }
        catch (Exception ex)
        {
            // Shutdown path — never throw. The next process simply won't surface previous-*
            // metadata; that is identical to a cold start and is harmless.
            _logger?.LogWarning(ex,
                "Failed to write host-process metadata to {FilePath}. " +
                "The next process will not surface previousStdioPid/previousExitedAt/previousRecycleReason. " +
                "This is non-fatal — the file is best-effort observability.",
                _filePath);
        }
    }

    private void EnsureLoaded()
    {
        // Idempotent: only the first call hits the disk. We don't bother locking the load
        // path because only a single thread (the host startup path) has any legitimate
        // reason to call EnsureLoaded before ConsumePrevious is reachable.
        if (_previousLoaded)
        {
            return;
        }

        _previousLoaded = true;

        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var record = JsonSerializer.Deserialize<HostProcessMetadataRecord>(json, s_jsonOptions);
            if (record is null || record.StdioPid <= 0 || string.IsNullOrWhiteSpace(record.ExitedAtUtc))
            {
                _logger?.LogDebug(
                    "host-process metadata at {FilePath} is missing required fields — discarding.",
                    _filePath);
                return;
            }

            // Parse the timestamp once so we can decide TTL vs preserve.
            DateTime? exitedAt = null;
            if (DateTime.TryParse(
                    record.ExitedAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                exitedAt = parsed;
            }

            // TTL guard: if the timestamp is older than StaleAfter, we still surface the pid
            // and timestamp (operators want SOMETHING) but normalize the reason to "unknown" —
            // a multi-day-old recycle reason is unreliable signal.
            var reason = record.RecycleReason;
            if (exitedAt is { } exitedTimestamp && (_utcNow() - exitedTimestamp) > _staleAfter)
            {
                reason = "unknown";
            }
            else if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "unknown";
            }

            // Best-effort: delete the on-disk record after we've loaded it. This prevents the
            // SAME record from being re-surfaced if the next host process crashes before it
            // gets a chance to write its own metadata. If the delete fails, the next read
            // will just see the same record again — annoying but not broken.
            try
            {
                File.Delete(_filePath);
            }
            catch (Exception deleteEx)
            {
                _logger?.LogDebug(deleteEx,
                    "Could not delete host-process metadata at {FilePath} after read — " +
                    "the record may resurface on the next startup, which is harmless.",
                    _filePath);
            }

            _previous = new HostProcessMetadataSnapshot(
                StdioPid: record.StdioPid,
                ExitedAtUtc: record.ExitedAtUtc,
                RecycleReason: reason);
        }
        catch (Exception ex)
        {
            // Best-effort read — corrupt JSON, IO errors, etc. Log and proceed with no
            // previous-* fields. This matches cold-start behavior.
            _logger?.LogDebug(ex,
                "Could not read host-process metadata at {FilePath}. " +
                "Treating as a cold start.",
                _filePath);
        }
    }

    /// <summary>
    /// Default file path: <c>%LOCALAPPDATA%/roslyn-mcp/host-process.json</c> on Windows,
    /// <c>$XDG_STATE_HOME/roslyn-mcp/host-process.json</c> on Linux/macOS, or the
    /// platform-equivalent <c>SpecialFolder.LocalApplicationData</c> as a final fallback.
    /// </summary>
    public static string ResolveDefaultPath()
    {
        // Prefer XDG_STATE_HOME on Linux/macOS so it follows the freedesktop spec. Falls back
        // to LOCALAPPDATA / SpecialFolder.LocalApplicationData when XDG isn't set.
        var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, DirectoryName, FileName);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            return Path.Combine(local, DirectoryName, FileName);
        }

        // Last-ditch fallback: temp directory. Survives the process but not a reboot — that's
        // OK because cross-reboot recycle metadata is not interesting (the OS killed everything).
        return Path.Combine(Path.GetTempPath(), DirectoryName, FileName);
    }
}

/// <summary>
/// On-disk shape of the host-process metadata record. Internal — the public consumer-facing
/// shape is <see cref="HostProcessMetadataSnapshot"/> after TTL normalization.
/// </summary>
internal sealed record HostProcessMetadataRecord(
    [property: JsonPropertyName("stdioPid")] int StdioPid,
    [property: JsonPropertyName("exitedAtUtc")] string ExitedAtUtc,
    [property: JsonPropertyName("recycleReason")] string RecycleReason);

/// <summary>
/// In-memory snapshot of the previous host-process metadata, normalized for the
/// <c>connection</c> wire shape. <c>RecycleReason</c> here is post-TTL: a stale record yields
/// <c>unknown</c> regardless of what was on disk.
/// </summary>
public sealed record HostProcessMetadataSnapshot(
    int StdioPid,
    string ExitedAtUtc,
    string RecycleReason);
