using System.Collections.Concurrent;
using Company.RoslynMcp.Core.Services;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class WorkspaceExecutionGate : IWorkspaceExecutionGate
{
    public const string LoadGateKey = "__load__";
    /// <summary>Gate for refactoring apply operations (no workspaceId in parameters).</summary>
    public const string ApplyGateKey = "__apply__";

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceGates = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string? gateKey, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(gateKey) ? LoadGateKey : gateKey;
        var gate = key == LoadGateKey ? _loadGate : _workspaceGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
