using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

internal sealed class CallbackPreparedUndoOperation(
    Func<CancellationToken, Task> publish,
    Func<CancellationToken, Task<bool>> restore) : IPreparedUndoOperation
{
    public void RetainRecovery() => throw new NotSupportedException("This workflow fixture never retains failed physical recovery.");

    public Task PublishAsync(CancellationToken cancellationToken) => publish(cancellationToken);
    public Task<bool> RestoreAsync(CancellationToken cancellationToken) => restore(cancellationToken);
}
