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

    /// <summary>
    /// When true, use a per-workspace <c>AsyncReaderWriterLock</c> instead of a per-workspace
    /// <c>SemaphoreSlim</c>. Reads against the same workspace can run concurrently, while
    /// writes remain exclusive against all other operations on the same workspace.
    /// Defaults to false (legacy mutex behavior, bit-for-bit identical to pre-flag releases).
    /// Set via <c>ROSLYNMCP_WORKSPACE_RW_LOCK</c> (must parse as <c>true</c>/<c>false</c> to override).
    /// </summary>
    public bool UseReaderWriterLock { get; init; } = false;
}
