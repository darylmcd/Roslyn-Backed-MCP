using System.Collections.Concurrent;
using System.Diagnostics;
using Nito.AsyncEx;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Serializes workspace operations using per-workspace gates so that concurrent callers
/// (e.g., multiple MCP sub-agents) cannot corrupt shared Roslyn state.
/// </summary>
/// <remarks>
/// A dedicated load gate is acquired by <see cref="RunLoadGateAsync{T}"/> for
/// <c>workspace_load</c>, <c>workspace_reload</c>, and <c>workspace_close</c> operations
/// because those operations replace the entire session state.
///
/// <para>
/// All other operations run under a per-workspace <see cref="AsyncReaderWriterLock"/> keyed
/// by the workspace session identifier: reads against the same workspace can run concurrently,
/// while writes are exclusive against all other operations on that workspace.
/// </para>
///
/// <para>
/// A global concurrency throttle bounds the total number of simultaneous operations across
/// all workspaces to <c>max(2, Environment.ProcessorCount)</c>. Each operation is subject to
/// a configurable per-request timeout (default 2 minutes) and a sliding-window rate limiter
/// (default 120 requests per 60 seconds).
/// </para>
/// </remarks>
public sealed class WorkspaceExecutionGate : IWorkspaceExecutionGate, IDisposable
{
    /// <summary>Global concurrency limit across all workspaces.</summary>
    private static readonly int MaxGlobalConcurrency = Math.Max(2, Environment.ProcessorCount);

    private readonly IWorkspaceManager _workspaceManager;
    private readonly TimeSpan _requestTimeout;
    private readonly int _rateLimitMax;
    private readonly TimeSpan _rateLimitWindow;
    private readonly StalenessPolicy _onStale;

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _globalThrottle = new(MaxGlobalConcurrency, MaxGlobalConcurrency);
    private readonly AsyncReaderWriterLockRegistry _rwLocks = new();

    /// <summary>
    /// Tracks whether the current async context already holds a <see cref="_globalThrottle"/>
    /// slot. Used to skip re-acquisition when a lifecycle method (e.g., <c>workspace_close</c>
    /// nesting <c>RunWriteAsync</c> inside <c>RunLoadGateAsync</c>) would otherwise consume two
    /// slots and risk deadlock when the throttle is saturated.
    /// </summary>
    private readonly AsyncLocal<int> _globalThrottleHeld = new();

    /// <summary>Sliding window rate limiter — stores timestamps of recent requests.</summary>
    private readonly ConcurrentQueue<long> _requestTimestamps = new();

    public WorkspaceExecutionGate(ExecutionGateOptions options, IWorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
        _requestTimeout = options.RequestTimeout;
        _rateLimitMax = options.RateLimitMaxRequests;
        _rateLimitWindow = options.RateLimitWindow;
        _onStale = options.OnStale;
    }

    public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        return RunPerWorkspaceAsync(workspaceId, isWrite: false, applyStalenessPolicy: true, action, ct);
    }

    public Task<T> RunWriteAsync<T>(
        string workspaceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct,
        bool applyStalenessPolicy = true)
    {
        return RunPerWorkspaceAsync(workspaceId, isWrite: true, applyStalenessPolicy, action, ct);
    }

    public async Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var queueStopwatch = Stopwatch.StartNew();
        EnforceRateLimit();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeout);
        var linked = timeoutCts.Token;

        return await WithGlobalThrottle(async () =>
        {
            await _loadGate.WaitAsync(linked).ConfigureAwait(false);
            try
            {
                return await RunWithMetricsAsync("load", queueStopwatch, () => action(linked)).ConfigureAwait(false);
            }
            finally
            {
                _loadGate.Release();
            }
        }, linked).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquire the global throttle for the duration of <paramref name="body"/>, skipping the
    /// physical wait if the current async context already holds a slot. The async-local depth
    /// counter ensures one async stack frame can nest two gate calls (e.g.,
    /// <c>workspace_close</c>'s <c>RunLoadGateAsync → RunWriteAsync</c>) without consuming two
    /// slots, which would risk deadlock under throttle saturation.
    /// </summary>
    private async Task<T> WithGlobalThrottle<T>(Func<Task<T>> body, CancellationToken ct)
    {
        if (_globalThrottleHeld.Value > 0)
        {
            _globalThrottleHeld.Value++;
            try
            {
                return await body().ConfigureAwait(false);
            }
            finally
            {
                _globalThrottleHeld.Value--;
            }
        }

        await _globalThrottle.WaitAsync(ct).ConfigureAwait(false);
        _globalThrottleHeld.Value = 1;
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            _globalThrottleHeld.Value = 0;
            _globalThrottle.Release();
        }
    }

    private async Task<T> RunPerWorkspaceAsync<T>(
        string workspaceId,
        bool isWrite,
        bool applyStalenessPolicy,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        // P3: Begin queue-time tracking immediately so the metrics summary captures rate-limit
        // wait, global throttle wait, and per-workspace lock wait under a single QueuedMs value.
        var queueStopwatch = Stopwatch.StartNew();

        EnforceRateLimit();

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("workspaceId is required.", nameof(workspaceId));
        }

        if (!_workspaceManager.ContainsWorkspace(workspaceId))
        {
            throw new KeyNotFoundException(
                $"Workspace '{workspaceId}' not found or has been closed. Active workspace IDs are listed by workspace_list.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeout);
        var linked = timeoutCts.Token;

        // Item 1: stale-gate policy. Checked once per call, before the per-workspace lock is
        // acquired, so an auto-reload path runs under the same load gate as explicit
        // workspace_reload calls — the reload's own write-locking is handled by WorkspaceManager.
        // workspace_close skips this: reloading requires the on-disk solution, which may already
        // be deleted while the in-memory session is still registered (F21 / missing worktree).
        if (applyStalenessPolicy)
        {
            await ApplyStalenessPolicyAsync(workspaceId, linked).ConfigureAwait(false);
        }

        return await WithGlobalThrottle(async () =>
        {
            var rwLock = _rwLocks.Get(workspaceId);
            if (isWrite)
            {
                using (await rwLock.WriterLockAsync(linked).ConfigureAwait(false))
                {
                    EnsureWorkspaceStillExists(workspaceId);
                    return await RunWithMetricsAsync("rw-lock", queueStopwatch, () => RunActionWithPostReloadRetryAsync(action, linked)).ConfigureAwait(false);
                }
            }

            using (await rwLock.ReaderLockAsync(linked).ConfigureAwait(false))
            {
                EnsureWorkspaceStillExists(workspaceId);
                return await RunWithMetricsAsync("rw-lock", queueStopwatch, () => RunActionWithPostReloadRetryAsync(action, linked)).ConfigureAwait(false);
            }
        }, linked).ConfigureAwait(false);
    }

    /// <summary>
    /// auto-reload-retry-inside-call: when <see cref="ApplyStalenessPolicyAsync"/> just
    /// auto-reloaded the workspace for this call (<see cref="AmbientGateMetrics.StaleAction"/>
    /// stamped <c>"auto-reloaded"</c>) and the action immediately fails with a transient
    /// stale-snapshot error (<c>"Document not found"</c> / <c>"not found in workspace"</c>),
    /// retry the action exactly once. The action's closure re-resolves through
    /// <see cref="IWorkspaceManager.GetCurrentSolution"/> on each invocation, so the second
    /// attempt sees the post-reload snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists:</b> the audit-driven workflow showed <c>format_document_preview</c>,
    /// <c>get_completions</c>, and <c>find_references_bulk</c> returning <c>"Document not found"</c>
    /// with <c>staleAction="auto-reloaded"</c> stamped — the reload had succeeded but the
    /// action's first snapshot read still observed the pre-reload solution (file watcher /
    /// MSBuildWorkspace settling window). The next-turn retry from the caller always succeeded.
    /// </para>
    /// <para>
    /// <b>Cap and scope:</b> the retry is hard-capped at 1 (no cascade — see plan Risks). It
    /// fires <i>only</i> when <c>StaleAction == "auto-reloaded"</c>, so error paths outside an
    /// auto-reload window propagate unchanged. Total time is bounded by the request timeout's
    /// linked cancellation token (<paramref name="ct"/>); a second attempt that runs out of
    /// budget surfaces the original exception via the standard catch-and-rethrow at the end.
    /// </para>
    /// <para>
    /// <b>Telemetry:</b> on retry, <see cref="AmbientGateMetrics.RetriedAfterReload"/> is set
    /// to <see langword="true"/> regardless of whether the retry succeeds. Callers see the
    /// flag in <c>_meta.retriedAfterReload</c> on both success envelopes (the read finally
    /// landed) and error envelopes (the workspace really did lose the document).
    /// </para>
    /// </remarks>
    private static async Task<T> RunActionWithPostReloadRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        try
        {
            return await action(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldRetryAfterAutoReload(ex))
        {
            // Stamp the retry flag BEFORE the second attempt so the response envelope shows
            // retriedAfterReload=true on both the success path AND the eventual-failure path.
            // The eventual-failure path is informative for telemetry — it confirms the retry
            // ran but the workspace genuinely lost the document.
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.RetriedAfterReload = true;
            }

            ct.ThrowIfCancellationRequested();
            return await action(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> is a transient stale-snapshot
    /// error <i>and</i> the current request just auto-reloaded the workspace. The intersection
    /// is deliberately narrow: any one condition alone is not enough to justify a retry.
    /// </summary>
    private static bool ShouldRetryAfterAutoReload(Exception ex)
    {
        if (AmbientGateMetrics.Current?.StaleAction != "auto-reloaded")
        {
            return false;
        }

        // Cancellation must always propagate immediately; a retry would just hit the same
        // cancelled token. OperationCanceledException covers TaskCanceledException too.
        if (ex is OperationCanceledException)
        {
            return false;
        }

        // Already retried once on this call — never cascade. Retry is hard-capped at 1.
        if (AmbientGateMetrics.Current?.RetriedAfterReload == true)
        {
            return false;
        }

        // Stale-snapshot errors surface in three exception shapes across the service layer:
        //   - InvalidOperationException("Document not found: ...") — DocumentResolution helper
        //   - FileNotFoundException("Document not found in workspace: ...") — EditorConfigService,
        //     FixAllService, FlowAnalysisService, OperationService
        //   - KeyNotFoundException("Document not found: ...") / similar — SyntaxTools,
        //     WorkspaceResources
        // Match by message substring so any service that follows the same naming convention
        // is covered without a hard-coded type list.
        var message = ex.Message ?? string.Empty;
        return ex is (InvalidOperationException or FileNotFoundException or KeyNotFoundException) &&
               (message.Contains("Document not found", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not found in workspace", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Item 1: enforces <see cref="ExecutionGateOptions.OnStale"/> when the underlying file
    /// watcher has flagged the workspace as stale. AutoReload transparently reloads, Warn
    /// leaves the snapshot alone but stamps <see cref="AmbientGateMetrics"/> so the response
    /// envelope surfaces a structured signal, Off is a no-op.
    /// </summary>
    private async Task ApplyStalenessPolicyAsync(string workspaceId, CancellationToken ct)
    {
        if (_onStale == StalenessPolicy.Off)
        {
            return;
        }

        if (!_workspaceManager.IsStale(workspaceId))
        {
            return;
        }

        var metrics = AmbientGateMetrics.Current;

        if (_onStale == StalenessPolicy.Warn)
        {
            if (metrics is not null)
            {
                metrics.StaleAction = "warn";
            }
            return;
        }

        // AutoReload: reload under the existing load-gate path so writes can't race with us.
        // dr-9-9-response-claims: only stamp the "auto-reloaded" signal when reload actually
        // succeeded. Pre-fix, the finally block stamped unconditionally — a reload that
        // short-circuited on KeyNotFoundException (workspace closed between stale check and
        // reload attempt) still advertised `staleAction: "auto-reloaded"` in the response
        // envelope, contradicting the fact that no reload ran.
        var reloadStopwatch = Stopwatch.StartNew();
        var reloaded = false;
        try
        {
            await _workspaceManager.ReloadAsync(workspaceId, ct).ConfigureAwait(false);
            reloaded = true;
        }
        catch (KeyNotFoundException)
        {
            // Workspace was closed between the stale check and the reload attempt — fall through
            // and let the caller's ContainsWorkspace check throw a user-facing error. Leave
            // StaleAction unset so the response does not falsely claim an auto-reload.
        }
        finally
        {
            reloadStopwatch.Stop();
            if (reloaded && metrics is not null)
            {
                metrics.StaleAction = "auto-reloaded";
                metrics.StaleReloadMs = reloadStopwatch.ElapsedMilliseconds;
            }
        }
    }

    /// <summary>
    /// P3: Capture queue-wait + lock-hold timing into <see cref="AmbientGateMetrics.Current"/>
    /// for the current request, then run the action. The queue stopwatch is stopped on entry
    /// (so its elapsed value reflects all wait time before the action ran) and a fresh hold
    /// stopwatch is started around the action body.
    /// </summary>
    private static async Task<T> RunWithMetricsAsync<T>(
        string gateMode, Stopwatch queueStopwatch, Func<Task<T>> body)
    {
        queueStopwatch.Stop();
        var queuedMs = queueStopwatch.ElapsedMilliseconds;
        var holdStopwatch = Stopwatch.StartNew();
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            holdStopwatch.Stop();
            var metrics = AmbientGateMetrics.Current;
            if (metrics is not null)
            {
                metrics.GateMode ??= gateMode;
                metrics.QueuedMs += queuedMs;
                metrics.HeldMs += holdStopwatch.ElapsedMilliseconds;
            }
        }
    }

    /// <summary>
    /// Re-check that a workspace still exists after acquiring its read/write lock. Closes the
    /// TOCTOU race where a reader passes the initial existence check, then waits behind a
    /// concurrent <c>workspace_close</c>'s write lock, then would otherwise operate on a closed
    /// workspace.
    /// </summary>
    private void EnsureWorkspaceStillExists(string workspaceId)
    {
        if (!_workspaceManager.ContainsWorkspace(workspaceId))
        {
            throw new KeyNotFoundException(
                $"Workspace '{workspaceId}' was closed while waiting for the workspace lock.");
        }
    }

    /// <summary>
    /// Drop the per-workspace lock entry from the registry for a workspace that is being closed.
    /// </summary>
    /// <remarks>
    /// The caller must already hold the per-workspace write lock so that no in-flight reader is
    /// operating against the workspace when its lock entry is removed (see
    /// <see cref="IWorkspaceExecutionGate.RemoveGate"/>).
    /// </remarks>
    public void RemoveGate(string workspaceId)
    {
        _rwLocks.Remove(workspaceId);
    }

    public void Dispose()
    {
        _loadGate.Dispose();
        _globalThrottle.Dispose();
    }

    /// <summary>
    /// Sliding-window rate limiter. Prunes expired timestamps and rejects when the window
    /// contains more than <see cref="_rateLimitMax"/> requests.
    /// </summary>
    private void EnforceRateLimit()
    {
        var now = Environment.TickCount64;
        var windowMs = (long)_rateLimitWindow.TotalMilliseconds;

        // Prune timestamps outside the window
        while (_requestTimestamps.TryPeek(out var oldest) && (now - oldest) > windowMs)
        {
            _requestTimestamps.TryDequeue(out _);
        }

        if (_requestTimestamps.Count >= _rateLimitMax)
        {
            throw new InvalidOperationException(
                $"Rate limit exceeded: {_rateLimitMax} requests per {_rateLimitWindow.TotalSeconds:F0}s window. " +
                $"Retry after a brief pause or increase ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS.");
        }

        _requestTimestamps.Enqueue(now);
    }
}
