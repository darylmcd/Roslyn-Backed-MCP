namespace RoslynMcp.Core.Services;

/// <summary>
/// Records pre-apply workspace state so that the most recent apply operation
/// can be reverted. Only the last operation per workspace is retained.
/// </summary>
public interface IUndoService
{
    /// <summary>
    /// Captures the current solution state before an apply operation.
    /// Call this immediately before mutating the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace being mutated.</param>
    /// <param name="description">Human-readable description of the operation being applied.</param>
    /// <param name="preApplySolution">The solution snapshot before mutation.</param>
    void CaptureBeforeApply(string workspaceId, string description, object preApplySolution);

    /// <summary>
    /// Returns metadata about the last undoable operation for the given workspace,
    /// or <see langword="null"/> if nothing is undoable.
    /// </summary>
    UndoEntry? GetLastOperation(string workspaceId);

    /// <summary>
    /// Reverts the most recent apply operation for the given workspace.
    /// Returns <see langword="true"/> if the revert succeeded.
    /// </summary>
    Task<bool> RevertAsync(string workspaceId, IWorkspaceManager workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears undo history for a workspace (e.g., on workspace close).
    /// </summary>
    void Clear(string workspaceId);
}

/// <summary>
/// Metadata about an undoable operation.
/// </summary>
/// <param name="WorkspaceId">The workspace the operation was applied to.</param>
/// <param name="Description">Human-readable description of the operation.</param>
/// <param name="AppliedAtUtc">When the operation was applied.</param>
public sealed record UndoEntry(
    string WorkspaceId,
    string Description,
    DateTime AppliedAtUtc);
