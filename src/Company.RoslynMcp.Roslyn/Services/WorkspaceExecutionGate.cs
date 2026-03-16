using System.Collections.Concurrent;
using Company.RoslynMcp.Core.Services;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class WorkspaceExecutionGate : IWorkspaceExecutionGate, IDisposable
{
    public const string LoadGateKey = "__load__";
    /// <summary>Gate for refactoring apply operations (no workspaceId in parameters).</summary>
    public const string ApplyGateKey = "__apply__";

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
