namespace RoslynMcp.Roslyn.Services;

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
}
