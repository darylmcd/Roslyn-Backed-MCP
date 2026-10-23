using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Preserves a completed session removal when subsequent cleanup fails.</summary>
internal sealed class WorkspaceCloseCleanupException(WorkspaceManager owner, string workspaceId,
    IEnumerable<Exception> failures) : AggregateException("The workspace was closed, but resource cleanup failed.", failures), IPublicMessageException
{
    public string PublicMessage => "The workspace was closed, but resource cleanup failed.";

    internal bool IsFor(IWorkspaceManager manager, string id)
        => ReferenceEquals(owner, manager) && string.Equals(workspaceId, id, StringComparison.Ordinal);

    internal Exception RemoveGatePreservingFailure(IWorkspaceExecutionGate gate)
    {
        try { gate.RemoveGate(workspaceId); }
        catch (Exception failure)
        {
            return new AggregateException("Workspace resource and execution-gate cleanup failed.", this, failure);
        }
        return this;
    }
}
