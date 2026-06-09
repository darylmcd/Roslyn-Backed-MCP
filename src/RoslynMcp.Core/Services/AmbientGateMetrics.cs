using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Ambient (AsyncLocal) request-scoped metrics builder. The host opens a scope before running
/// each tool action; the workspace execution gate writes into the current builder; the host
/// snapshots the builder after the action and merges the result into the response JSON as
/// <c>_meta</c>. Designed so service code does not need to take an explicit dependency on the
/// host serialization layer (P3 in the audit-driven plan; companion to <see cref="GateMetricsDto"/>).
/// </summary>
public static class AmbientGateMetrics
{
    private static readonly AsyncLocal<GateMetricsBuilder?> _current = new();

    /// <summary>The current request's metrics builder, or <see langword="null"/> outside a scope.</summary>
    public static GateMetricsBuilder? Current => _current.Value;

    /// <summary>
    /// Opens a metrics scope for the current async context. Writes from nested code (e.g. the
    /// gate timing) flow into <see cref="Current"/> until the returned scope is disposed. Nested
    /// scopes restore the parent on dispose so re-entrant tool calls do not corrupt each other.
    /// </summary>
    public static IDisposable BeginRequest()
    {
        var previous = _current.Value;
        _current.Value = new GateMetricsBuilder();
        return new Scope(previous);
    }

    /// <summary>Returns an immutable snapshot of the current builder, or <see langword="null"/>.</summary>
    public static GateMetricsDto? Snapshot() => _current.Value?.ToDto();

    private sealed class Scope : IDisposable
    {
        private readonly GateMetricsBuilder? _previous;
        private bool _disposed;

        public Scope(GateMetricsBuilder? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}

/// <summary>
/// Mutable per-request metrics accumulator. Writers (the gate) update the fields directly;
/// readers (the host) call <see cref="ToDto"/> to get an immutable snapshot.
/// </summary>
public sealed class GateMetricsBuilder
{
    public string? GateMode { get; set; }
    public long QueuedMs { get; set; }
    public long HeldMs { get; set; }
    public int? HeartbeatCount { get; set; }

    /// <summary>
    /// Wall-clock milliseconds for the entire tool action. Captured by the tool error handler
    /// at the start/end of <c>ExecuteAsync</c> so every tool — reader or writer — surfaces
    /// the same per-request timing without each service having to add its own Stopwatch.
    /// </summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Set by <c>WorkspaceExecutionGate</c> when the workspace was stale at gate entry:
    /// <c>auto-reloaded</c> or <c>warn</c>. <see langword="null"/> otherwise.
    /// </summary>
    public string? StaleAction { get; set; }

    /// <summary>Milliseconds spent reloading when <see cref="StaleAction"/> is <c>auto-reloaded</c>.</summary>
    public long? StaleReloadMs { get; set; }

    /// <summary>
    /// auto-reload-retry-inside-call: <see langword="true"/> when the workspace execution gate
    /// retried the action once after an auto-reload (because the first attempt failed with a
    /// transient stale-snapshot error such as <c>"Document not found"</c>). Stays <see langword="null"/>
    /// when no retry occurred or when the retry itself failed.
    /// </summary>
    public bool? RetriedAfterReload { get; set; }

    /// <summary>
    /// workspace-reloaded-during-call-conflates-notfound: set to <see langword="true"/> when
    /// the gate retried after an auto-reload and the second attempt also failed with a
    /// "Document not found" error. This confirms the failure is a genuine bad path, not a
    /// transient stale-snapshot race. <see cref="RoslynMcp.Host.Stdio.Tools.ToolErrorHandler"/>
    /// checks this flag before emitting <c>WorkspaceReloadedDuringCall</c> so that a bad-path
    /// error concurrent with an auto-reload returns <c>category=NotFound</c> (the true cause)
    /// instead of the misleading implementation-detail category.
    /// </summary>
    public bool? ReloadConfirmedNotFound { get; set; }

    /// <summary>
    /// workspace-load-uses-cache-fast-path: set by <c>WorkspaceManager</c> during
    /// <c>workspace_load</c>/<c>workspace_reload</c>. <see langword="true"/> when the on-disk
    /// <see cref="IWorkspaceCacheStore"/> returned a usable entry whose persisted project graph
    /// matched the post-MSBuild project graph (the warm-cache fast path skipped the restore-race
    /// wait and refreshed the cached metadata-reference timestamps in place). <see langword="false"/>
    /// when the cache miss path ran (cold load wrote a fresh entry). <see langword="null"/> when
    /// no cache store was wired (legacy callers / test fixtures that constructed
    /// <c>WorkspaceManager</c> without a store) or the request did not touch the load gate.
    /// Lets future profiling isolate the warm-cache path from the cold path without external
    /// instrumentation.
    /// </summary>
    public bool? CacheHit { get; set; }

    /// <summary>
    /// workspace-id-omitted-single-resolve: set by <c>StructuredCallToolFilter</c> when a
    /// read-only, non-destructive tool is dispatched with <c>workspaceId</c> omitted/empty.
    /// <c>explicit</c> (caller supplied an id), <c>single-workspace</c> (resolved to the one
    /// loaded workspace), or <c>fast-fail</c> (≥2 loaded — structured error returned).
    /// <see langword="null"/> when the request did not pass through the auto-resolution path.
    /// </summary>
    public string? AutoResolution { get; set; }

    public GateMetricsDto ToDto() => new(GateMode, QueuedMs, HeldMs, HeartbeatCount, ElapsedMs, StaleAction, StaleReloadMs, RetriedAfterReload, CacheHit, ReloadConfirmedNotFound, AutoResolution);
}
