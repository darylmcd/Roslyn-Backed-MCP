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
    }

    public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        return RunPerWorkspaceAsync(workspaceId, isWrite: false, action, ct);
    }

    public Task<T> RunWriteAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        return RunPerWorkspaceAsync(workspaceId, isWrite: true, action, ct);
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

        return await WithGlobalThrottle(async () =>
        {
            var rwLock = _rwLocks.Get(workspaceId);
            if (isWrite)
            {
                using (await rwLock.WriterLockAsync(linked).ConfigureAwait(false))
                {
                    EnsureWorkspaceStillExists(workspaceId);
                    return await RunWithMetricsAsync("rw-lock", queueStopwatch, () => action(linked)).ConfigureAwait(false);
                }
            }

            using (await rwLock.ReaderLockAsync(linked).ConfigureAwait(false))
            {
                EnsureWorkspaceStillExists(workspaceId);
                return await RunWithMetricsAsync("rw-lock", queueStopwatch, () => action(linked)).ConfigureAwait(false);
            }
        }, linked).ConfigureAwait(false);
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
