namespace RoslynMcp.Core.Models;

/// <summary>
/// Per-request observability summary attached to every tool response under the <c>_meta</c> key.
/// Lets clients that don't surface MCP <c>notifications/message</c> log entries still observe
/// concurrency-gate state, queue wait time, and lock-hold time. See FLAG-D root cause discussion.
/// </summary>
/// <param name="GateMode">
/// The gate this request was serialized through: <c>rw-lock</c> (per-workspace reader/writer
/// lock used by every read/write tool) or <c>load</c> (the global load gate used by
/// <c>workspace_load</c>, <c>workspace_reload</c>, and <c>workspace_close</c>).
/// <see langword="null"/> when the request did not pass through any workspace gate
/// (e.g. workspace-independent tools like <c>analyze_snippet</c>).
/// </param>
/// <param name="QueuedMs">Total milliseconds spent waiting in the rate limiter, global throttle, and per-workspace lock queues before the action ran.</param>
/// <param name="HeldMs">Total milliseconds the per-workspace lock (or load gate) was held while the action executed.</param>
/// <param name="HeartbeatCount">For long-running operations that emit progress heartbeats, the number of heartbeats observed. <see langword="null"/> when the tool does not emit progress.</param>
/// <param name="ElapsedMs">Total wall-clock milliseconds the tool action took, including queue + lock-hold + service work. Lets concurrency audits compute speedup ratios from inside the agent loop without external instrumentation.</param>
public sealed record GateMetricsDto(
    string? GateMode,
    long QueuedMs,
    long HeldMs,
    int? HeartbeatCount,
    long ElapsedMs = 0);
