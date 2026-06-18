using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Host.Stdio.Services;

/// <summary>
/// Indirection for "latest published version of this server" — lets tests substitute a
/// canned value without touching the real NuGet flat container. Implemented by
/// <see cref="NuGetVersionChecker"/> in production.
/// </summary>
public interface ILatestVersionProvider
{
    /// <inheritdoc cref="NuGetVersionChecker.GetLatestVersion"/>
    string? GetLatestVersion();

    /// <inheritdoc cref="NuGetVersionChecker.LastCheckStatus"/>
    VersionCheckStatus LastCheckStatus { get; }

    /// <inheritdoc cref="NuGetVersionChecker.LastCheckedAt"/>
    DateTime? LastCheckedAt { get; }
}

/// <summary>
/// Outcome of the most recent NuGet latest-version check. Surfaced via
/// <see cref="NuGetVersionChecker.LastCheckStatus"/> for observability — distinguishes the
/// states a bare null return previously collapsed together (never started, in flight,
/// succeeded, network/parse failure, timeout). Surfaced to operators via
/// <c>server_info.update.checkStatus</c>.
/// </summary>
public enum VersionCheckStatus
{
    /// <summary>No fetch has been attempted yet.</summary>
    NeverChecked,

    /// <summary>A background fetch is currently in flight.</summary>
    Pending,

    /// <summary>The most recent fetch parsed a latest stable version.</summary>
    Succeeded,

    /// <summary>The most recent fetch failed (network/HTTP/parse error).</summary>
    Failed,

    /// <summary>The most recent fetch exceeded the bounded HTTP timeout.</summary>
    TimedOut,
}

/// <summary>
/// Lazily checks NuGet for the latest published version of Darylmcd.RoslynMcp
/// and caches the result. Never throws — returns null when the check is pending,
/// failed, or timed out.
/// </summary>
public sealed class NuGetVersionChecker : ILatestVersionProvider
{
    private const string PackageId = "darylmcd.roslynmcp";
    private static readonly Uri FlatContainerUri = new($"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json");
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly ILogger<NuGetVersionChecker> _logger;
    private readonly object _lock = new();
    private string? _latestVersion;
    private DateTime _checkedAtUtc;
    private Task? _pendingCheck;
    private VersionCheckStatus _lastCheckStatus = VersionCheckStatus.NeverChecked;
    private DateTime? _lastCheckedAtUtc;

    /// <param name="http">HTTP client used to query the NuGet flat container.</param>
    /// <param name="logger">
    /// Optional logger. Defaults to <see cref="NullLogger{T}"/> so unit tests can construct
    /// the checker directly without wiring a logging provider.
    /// </param>
    public NuGetVersionChecker(HttpClient http, ILogger<NuGetVersionChecker>? logger = null)
    {
        _http = http;
        _logger = logger ?? NullLogger<NuGetVersionChecker>.Instance;
    }

    /// <summary>
    /// Outcome of the most recent (or in-flight) latest-version check. Observability signal
    /// only — never affects the non-blocking contract of <see cref="GetLatestVersion"/>.
    /// </summary>
    public VersionCheckStatus LastCheckStatus
    {
        get { lock (_lock) { return _lastCheckStatus; } }
    }

    /// <summary>
    /// UTC timestamp of the last completed check (success or failure), or null if no check
    /// has finished yet or a refresh is currently pending.
    /// </summary>
    public DateTime? LastCheckedAt
    {
        get { lock (_lock) { return _lastCheckedAtUtc; } }
    }

    /// <summary>
    /// Test-only seam exposing the in-flight background fetch task so tests can await its
    /// completion deterministically instead of polling <see cref="LastCheckStatus"/> on a
    /// wall-clock loop. Captured under <c>_lock</c> to avoid a torn read; null when no fetch
    /// has been started. No production behavior depends on this.
    /// </summary>
    internal Task? PendingCheckForTest
    {
        get { lock (_lock) { return _pendingCheck; } }
    }

    /// <summary>
    /// Returns the latest stable version string from NuGet, or null if the check
    /// hasn't completed yet / failed / is stale and a refresh is in progress.
    /// Calling this kicks off a background fetch on first access and after cache expiry.
    /// </summary>
    public string? GetLatestVersion()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var cacheValid = _latestVersion is not null
                             && (now - _checkedAtUtc) < CacheDuration;

            if (cacheValid)
                return _latestVersion;

            var recentTerminalFailure = _lastCheckedAtUtc is not null
                                        && _lastCheckStatus is VersionCheckStatus.Failed or VersionCheckStatus.TimedOut
                                        && (now - _lastCheckedAtUtc.Value) < CacheDuration;
            if (recentTerminalFailure)
                return null;

            // Kick off a background refresh if one isn't already running
            if (_pendingCheck is null || _pendingCheck.IsCompleted)
            {
                _lastCheckStatus = VersionCheckStatus.Pending;
                _lastCheckedAtUtc = null;
                _pendingCheck = Task.Run(FetchLatestVersionAsync);
            }

            // Return stale value (or null on first call) while refresh runs
            return _latestVersion;
        }
    }

    private async Task FetchLatestVersionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(HttpTimeout);
            var json = await _http.GetStringAsync(FlatContainerUri, cts.Token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("versions", out var versions))
            {
                // Response parsed but lacked the expected shape — treat as a failed check.
                RecordFailure(VersionCheckStatus.Failed);
                _logger.LogDebug("NuGet version check returned no 'versions' array for {PackageId}", PackageId);
                return;
            }

            // Find the latest stable version (no '-' prerelease tag) by semantic comparison.
            // NuGet flat container returns versions in ascending order, but we use explicit
            // Version comparison rather than relying on array ordering for robustness.
            Version? bestParsed = null;
            string? latest = null;
            foreach (var v in versions.EnumerateArray())
            {
                var ver = v.GetString();
                if (ver is not null && !ver.Contains('-') && Version.TryParse(ver, out var parsed))
                {
                    if (bestParsed is null || parsed > bestParsed)
                    {
                        bestParsed = parsed;
                        latest = ver;
                    }
                }
            }

            if (latest is not null)
            {
                lock (_lock)
                {
                    _latestVersion = latest;
                    _checkedAtUtc = DateTime.UtcNow;
                    _lastCheckStatus = VersionCheckStatus.Succeeded;
                    _lastCheckedAtUtc = _checkedAtUtc;
                }
            }
            else
            {
                // No stable version found among the returned entries.
                RecordFailure(VersionCheckStatus.Failed);
                _logger.LogDebug("NuGet version check found no stable version for {PackageId}", PackageId);
            }
        }
        catch (OperationCanceledException ex)
        {
            // Bounded HTTP timeout elapsed (linked CTS) — distinct from other failures.
            RecordFailure(VersionCheckStatus.TimedOut);
            _logger.LogDebug(ex, "NuGet version check timed out after {Timeout} for {PackageId}", HttpTimeout, PackageId);
        }
        catch (Exception ex)
        {
            // Best-effort: never propagate. Record + log so the failure isn't silent.
            RecordFailure(VersionCheckStatus.Failed);
            _logger.LogDebug(ex, "NuGet version check failed for {PackageId}", PackageId);
        }
    }

    private void RecordFailure(VersionCheckStatus status)
    {
        lock (_lock)
        {
            _lastCheckStatus = status;
            _lastCheckedAtUtc = DateTime.UtcNow;
        }
    }
}
