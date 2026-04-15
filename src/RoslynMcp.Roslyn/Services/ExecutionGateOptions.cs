namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Controls how <see cref="WorkspaceExecutionGate"/> responds when a workspace is marked stale
/// by <see cref="RoslynMcp.Core.Services.IFileWatcherService"/> at tool-call time.
/// </summary>
public enum StalenessPolicy
{
    /// <summary>
    /// Transparently reload the workspace before the tool runs. First stale call pays the
    /// reload cost; subsequent calls see fresh data. Default.
    /// </summary>
    AutoReload = 0,

    /// <summary>
    /// Proceed against the stale snapshot but annotate the response envelope with a
    /// <see cref="StaleWorkspaceWarning"/>. Useful when auto-reload latency is unacceptable
    /// but the caller still wants an explicit signal.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Ignore the stale flag entirely. Restores pre-v1.17 behavior for callers that need it.
    /// </summary>
    Off = 2,
}

/// <summary>
/// Configuration options for <see cref="WorkspaceExecutionGate"/> rate limiting and timeouts.
/// </summary>
public sealed class ExecutionGateOptions
{
    /// <summary>
    /// Maximum number of tool invocations permitted per sliding window.
    /// Defaults to 120 (generous for a single stdio client).
    /// Set via <c>ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS</c>.
    /// </summary>
    public int RateLimitMaxRequests { get; init; } = 120;

    /// <summary>
    /// Sliding window duration for rate limiting.
    /// Defaults to 60 seconds.
    /// Set via <c>ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS</c>.
    /// </summary>
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Per-request timeout applied to every tool invocation.
    /// Defaults to 2 minutes.
    /// Set via <c>ROSLYNMCP_REQUEST_TIMEOUT_SECONDS</c>.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How the gate responds when a workspace is marked stale by the file watcher.
    /// Defaults to <see cref="StalenessPolicy.AutoReload"/>.
    /// Set via <c>ROSLYNMCP_ON_STALE</c> (values: <c>auto-reload</c>, <c>warn</c>, <c>off</c>).
    /// </summary>
    public StalenessPolicy OnStale { get; init; } = StalenessPolicy.AutoReload;
}
