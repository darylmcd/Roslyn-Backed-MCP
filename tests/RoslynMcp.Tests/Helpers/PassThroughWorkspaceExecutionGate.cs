using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Test-only gate that supports read dispatch and fails closed for every mutation or lifecycle
/// route. Use only where the subject under test must remain read-only.
/// </summary>
internal sealed class PassThroughWorkspaceExecutionGate : IWorkspaceExecutionGate
{
    public Task<T> RunReadAsync<T>(
        string workspaceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct) => action(ct);

    public Task<T> RunWriteAsync<T>(
        string workspaceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct,
        bool applyStalenessPolicy = true) => throw Unsupported(nameof(RunWriteAsync));

    public Task<T> RunLoadGateAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct) => throw Unsupported(nameof(RunLoadGateAsync));

    public void RemoveGate(string workspaceId) => throw Unsupported(nameof(RemoveGate));

    private static NotSupportedException Unsupported(string member) =>
        new($"{nameof(PassThroughWorkspaceExecutionGate)} does not support {member}.");
}
