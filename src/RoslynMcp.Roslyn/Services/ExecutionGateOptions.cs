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
    /// <para>
    /// <b>Default changed to <c>true</c> in the phase-2 flip</b> after the dual-mode ITChatBot
    /// audit pair (20260407T211317Z rw-lock + 20260407T213736Z legacy-mutex) showed that
    /// FLAG-021 (<c>workspace_close</c> <c>ObjectDisposedException</c>) is legacy-mutex-specific:
    /// the rw-lock <c>AsyncReaderWriterLockRegistry.Remove</c> path drops the dictionary entry
    /// without disposing any <see cref="SemaphoreSlim"/>, avoiding the race. The legacy branch
    /// remains available via <c>ROSLYNMCP_WORKSPACE_RW_LOCK=false</c> as an opt-out escape hatch.
    /// </para>
    /// Set via <c>ROSLYNMCP_WORKSPACE_RW_LOCK</c> (must parse as <c>true</c>/<c>false</c> to override).
    /// </summary>
    public bool UseReaderWriterLock { get; init; } = true;
}
