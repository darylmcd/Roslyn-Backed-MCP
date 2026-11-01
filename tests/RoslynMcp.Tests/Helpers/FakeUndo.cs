using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

internal sealed class FakeUndo : IUndoService
{
    public int RevertCalls;
    public CancellationToken LastRevertToken;
    public bool RevertReturns = true;
    public Exception? RevertThrows;
    public RevertBySequenceResult SequenceResult =
        new(false, null, Array.Empty<string>(), "unknown-sequence", null);

    public Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        RevertCalls++;
        LastRevertToken = cancellationToken;
        if (RevertThrows is not null)
        {
            throw RevertThrows;
        }

        return Task.FromResult(RevertReturns);
    }

    public void CaptureBeforeApply(string workspaceId, string description, object? preApplySolution,
        IReadOnlyList<FileSnapshotDto>? fileSnapshots = null) { }

    public UndoEntry? GetLastOperation(string workspaceId) => null;

    public Task<RevertBySequenceResult> RevertBySequenceAsync(string workspaceId, int sequenceNumber,
        CancellationToken cancellationToken = default) => Task.FromResult(SequenceResult);

    public void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles) { }

    public void Clear(string workspaceId) { }
}
