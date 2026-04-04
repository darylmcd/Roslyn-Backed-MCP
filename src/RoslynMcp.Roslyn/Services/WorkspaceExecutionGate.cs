using System.Collections.Concurrent;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Serializes workspace operations using per-workspace semaphores so that concurrent callers
/// (e.g., multiple MCP sub-agents) cannot corrupt shared Roslyn state.
/// </summary>
/// <remarks>
/// A dedicated load gate (<see cref="IWorkspaceExecutionGate.LoadGateKey"/>) is used for <c>workspace_load</c> and
/// <c>workspace_reload</c> operations because those operations replace the entire session state.
/// All other operations run under a per-workspace gate keyed by the workspace session identifier.
/// A global concurrency throttle bounds the total number of simultaneous operations across all
/// workspaces to <c>max(2, Environment.ProcessorCount)</c>.
/// Each operation is subject to a configurable per-request timeout (default 2 minutes) and
/// a sliding-window rate limiter (default 120 requests per 60 seconds).
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceGates = new(StringComparer.Ordinal);

    /// <summary>Sliding window rate limiter — stores timestamps of recent requests.</summary>
    private readonly ConcurrentQueue<long> _requestTimestamps = new();

    public WorkspaceExecutionGate(ExecutionGateOptions options, IWorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
        _requestTimeout = options.RequestTimeout;
        _rateLimitMax = options.RateLimitMaxRequests;
        _rateLimitWindow = options.RateLimitWindow;
    }

    public async Task<T> RunAsync<T>(string? gateKey, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        // Rate limiting: enforce sliding-window request cap
        EnforceRateLimit();

        var key = string.IsNullOrWhiteSpace(gateKey) ? IWorkspaceExecutionGate.LoadGateKey : gateKey;
        if (key != IWorkspaceExecutionGate.LoadGateKey)
        {
            if (key.StartsWith("__apply__:", StringComparison.Ordinal))
            {
                var applyWs = key["__apply__:".Length..];
                if (!_workspaceManager.ContainsWorkspace(applyWs))
                {
                    throw new KeyNotFoundException(
                        $"Workspace '{applyWs}' not found or has been closed. Active workspace IDs are listed by workspace_list.");
                }
            }
            else if (key != "__apply__" && !_workspaceManager.ContainsWorkspace(key))
            {
                throw new KeyNotFoundException(
                    $"Workspace '{key}' not found or has been closed. Active workspace IDs are listed by workspace_list.");
            }
        }

        var gate = key == IWorkspaceExecutionGate.LoadGateKey ? _loadGate : _workspaceGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        // Apply per-request timeout and global concurrency throttle
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeout);
        var linked = timeoutCts.Token;

        await _globalThrottle.WaitAsync(linked).ConfigureAwait(false);
        try
        {
            await gate.WaitAsync(linked).ConfigureAwait(false);
            try
            {
                return await action(linked).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            _globalThrottle.Release();
        }
    }

    /// <summary>
    /// Remove and dispose the gate for a workspace that is being closed.
    /// </summary>
    public void RemoveGate(string workspaceId)
    {
        if (_workspaceGates.TryRemove(workspaceId, out var gate))
        {
            gate.Dispose();
        }
    }

    public void Dispose()
    {
        _loadGate.Dispose();
        _globalThrottle.Dispose();
        foreach (var kvp in _workspaceGates)
        {
            kvp.Value.Dispose();
        }
        _workspaceGates.Clear();
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
