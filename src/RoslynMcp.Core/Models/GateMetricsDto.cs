namespace RoslynMcp.Core.Models;

/// <summary>
/// Per-request observability summary attached to every tool response under the <c>_meta</c> key.
/// Lets clients observe concurrency-gate state, queue wait time, and lock-hold time without
/// depending on a protocol logging capability. See FLAG-D root cause discussion.
/// </summary>
/// <param name="GateMode">
/// The gate this request was serialized through: <c>rw-lock</c> (per-workspace reader/writer
/// lock used by every read/write tool) or <c>load</c> (the global load gate used by
/// <c>workspace_load</c>, <c>workspace_reload</c>, and <c>workspace_close</c>).
/// <see langword="null"/> when the request did not pass through any workspace gate
/// (e.g. workspace-independent tools like <c>analyze_snippet</c>).
/// </param>
/// <param name="QueuedMs">Total milliseconds spent waiting in the rate limiter, global throttle, and per-workspace lock queues before the action ran.</param>
/// <param name="HeldMs">Wall-clock milliseconds covered by one or more held workspace/load gates. Nested or concurrent gate holds are counted once, so this value does not exceed request elapsed time.</param>
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
/// <param name="CacheHit">
/// workspace-load-uses-cache-fast-path: set by <c>WorkspaceManager</c> during
/// <c>workspace_load</c>/<c>workspace_reload</c>. <see langword="true"/> when the on-disk
/// <c>IWorkspaceCacheStore</c> returned a usable entry whose persisted project graph matched
/// the post-MSBuild project graph (the warm-cache fast path skipped the restore-race wait
/// and refreshed the cached metadata-reference timestamps in place). <see langword="false"/>
/// when the cache miss path ran (cold load wrote a fresh entry). <see langword="null"/> when
/// no cache store was wired (legacy callers / test fixtures that constructed
/// <c>WorkspaceManager</c> without a store) or the request did not touch the load gate.
/// Lets future profiling isolate the warm-cache path from the cold path without external
/// instrumentation.
/// </param>
/// <param name="ReloadConfirmedNotFound">
/// workspace-reloaded-during-call-conflates-notfound: <see langword="true"/> when the gate
/// retried after an auto-reload and the second attempt also returned a "Document not found"
/// error, confirming the failure is a genuine bad path rather than a transient stale-snapshot
/// race. When this is set, <c>ToolErrorHandler</c> emits <c>category=NotFound</c> instead of
/// <c>WorkspaceReloadedDuringCall</c> so callers routing on category see the correct signal.
/// <see langword="null"/> in all other cases (common path: no retry, or retry succeeded).
/// </param>
/// <param name="AutoResolution">
/// workspace-id-omitted-single-resolve: records how the read-path middleware resolved an
/// omitted/empty <c>workspaceId</c> on a read-only, non-destructive tool before dispatch:
/// <c>explicit</c> (the caller supplied an id; left untouched), <c>single-workspace</c> (id
/// omitted and exactly one workspace was loaded, so the middleware patched it in), or
/// <c>fast-fail</c> (id omitted and two-or-more workspaces — or two-or-more discoverable
/// candidate solutions — so the middleware returned a structured error listing them instead
/// of guessing), or <c>auto-loaded</c> (id omitted, zero workspaces loaded, and a single
/// solution was discovered from the call context and loaded on demand before dispatch), or
/// <c>request-state</c> (a modern MRTR retry restored the workspace selected on an earlier
/// round trip instead of consulting mutable ambient workspace state).
/// <see langword="null"/> when the call did not go through the auto-resolution path (a
/// write/destructive tool, a tool with no <c>workspaceId</c> parameter, or zero workspaces
/// loaded with no discoverable solution — that last case is left for the binder/elicitation).
/// </param>
/// <param name="AutoLoadElapsedMs">
/// workspace-auto-load-on-demand: milliseconds spent discovering and loading a workspace on
/// demand when <paramref name="AutoResolution"/> is <c>auto-loaded</c>. <see langword="null"/>
/// otherwise. Lets profiling isolate the cold on-demand-load cost from ordinary request timing.
/// </param>
public sealed record GateMetricsDto(
    string? GateMode,
    long QueuedMs,
    long HeldMs,
    int? HeartbeatCount,
    long ElapsedMs = 0,
    string? StaleAction = null,
    long? StaleReloadMs = null,
    bool? RetriedAfterReload = null,
    bool? CacheHit = null,
    bool? ReloadConfirmedNotFound = null,
    string? AutoResolution = null,
    long? AutoLoadElapsedMs = null);
