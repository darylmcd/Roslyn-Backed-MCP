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
/// <param name="StaleAction">
/// When the workspace was marked stale by the file watcher before this call ran, records the
/// staleness policy that took effect: <c>auto-reloaded</c> (the gate reloaded the workspace
/// transparently and the tool saw fresh state), <c>warn</c> (the tool ran against a stale
/// snapshot with a structured warning attached), or <see langword="null"/> when the call was
/// not stale or the policy was set to <c>off</c>.
/// </param>
/// <param name="StaleReloadMs">Milliseconds spent inside <c>workspace_reload</c> when <paramref name="StaleAction"/> is <c>auto-reloaded</c>. <see langword="null"/> otherwise.</param>
/// <param name="RetriedAfterReload">
/// auto-reload-retry-inside-call: <see langword="true"/> when the workspace execution gate
/// retried the action once after an auto-reload because the first attempt failed with a
/// transient stale-snapshot error (e.g., <c>"Document not found"</c>). Surfaces in the
/// response envelope so callers can correlate "auto-reloaded" calls that needed an
/// internal second pass to succeed; <see langword="null"/> when no retry occurred (the
/// common case). When the retry itself fails, the original exception is propagated and
/// this flag remains <see langword="null"/> — callers see the structured error without a
/// false success signal.
/// </param>
public sealed record GateMetricsDto(
    string? GateMode,
    long QueuedMs,
    long HeldMs,
    int? HeartbeatCount,
    long ElapsedMs = 0,
    string? StaleAction = null,
    long? StaleReloadMs = null,
    bool? RetriedAfterReload = null);
