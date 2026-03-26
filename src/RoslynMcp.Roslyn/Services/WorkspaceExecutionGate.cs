using System.Collections.Concurrent;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Serializes workspace operations using per-workspace semaphores so that concurrent callers
/// (e.g., multiple MCP sub-agents) cannot corrupt shared Roslyn state.
/// </summary>
/// <remarks>
/// A dedicated load gate (<see cref="LoadGateKey"/>) is used for <c>workspace_load</c> and
/// <c>workspace_reload</c> operations because those operations replace the entire session state.
/// All other operations run under a per-workspace gate keyed by the workspace session identifier.
/// A global concurrency throttle bounds the total number of simultaneous operations across all
/// workspaces to <c>max(2, Environment.ProcessorCount)</c>.
/// Each operation is also subject to a 2-minute per-request timeout.
/// </remarks>
public sealed class WorkspaceExecutionGate : IWorkspaceExecutionGate, IDisposable
{
    public const string LoadGateKey = "__load__";

    /// <summary>Default per-request timeout (2 minutes).</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Global concurrency limit across all workspaces.</summary>
    private static readonly int MaxGlobalConcurrency = Math.Max(2, Environment.ProcessorCount);

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _globalThrottle = new(MaxGlobalConcurrency, MaxGlobalConcurrency);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceGates = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string? gateKey, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(gateKey) ? LoadGateKey : gateKey;
        var gate = key == LoadGateKey ? _loadGate : _workspaceGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        // Apply per-request timeout and global concurrency throttle
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultTimeout);
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
}
