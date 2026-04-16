using System.Text.Json;

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
    private readonly object _lock = new();
    private string? _latestVersion;
    private DateTime _checkedAtUtc;
    private Task? _pendingCheck;

    public NuGetVersionChecker(HttpClient http)
    {
        _http = http;
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
            var cacheValid = _latestVersion is not null
                             && (DateTime.UtcNow - _checkedAtUtc) < CacheDuration;

            if (cacheValid)
                return _latestVersion;

            // Kick off a background refresh if one isn't already running
            if (_pendingCheck is null || _pendingCheck.IsCompleted)
                _pendingCheck = Task.Run(FetchLatestVersionAsync);

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
                return;

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
                }
            }
        }
        catch
        {
            // Swallow — version check is best-effort
        }
    }
}
