using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IChangeTracker
{
    /// <summary>
    /// Raised after <see cref="RecordChange"/> appends a new change entry. Subscribers receive
    /// the recorded entry so they can synchronize their own per-apply state with the canonical
    /// sequence number assigned here. Used by <see cref="IUndoService"/> to commit its pending
    /// pre-apply snapshot to the per-workspace revert history (see
    /// <c>revert-apply-by-sequence-number</c>). Handlers must not throw — exceptions are
    /// swallowed by the tracker so a misbehaving subscriber cannot break the apply path.
    /// </summary>
    event Action<ChangeRecordedEventArgs>? ChangeRecorded;

    /// <summary>
    /// Appends a change entry to the workspace ledger and raises <see cref="ChangeRecorded"/>.
    /// </summary>
    /// <param name="capture">
    /// The <see cref="IUndoService.CaptureBeforeApply"/> id taken by the SAME logical operation, or
    /// <see cref="UndoCaptureId.None"/> when the record owns no capture (a successful no-effect audit).
    /// Only a matching id promotes the pending snapshot into the revert history.
    /// </param>
    void RecordChange(string workspaceId, string description,
        IReadOnlyList<string> affectedFiles, string toolName, UndoCaptureId capture);

    IReadOnlyList<WorkspaceChangeDto> GetChanges(string workspaceId);

    void Clear(string workspaceId);
}

/// <summary>
/// Payload for <see cref="IChangeTracker.ChangeRecorded"/>. Mirrors the fields of the
/// just-appended <see cref="WorkspaceChangeDto"/> plus the workspace identifier, so subscribers
/// can synchronize their per-workspace state without re-querying the tracker.
/// </summary>
/// <param name="WorkspaceId">The workspace the change was applied to.</param>
/// <param name="SequenceNumber">The sequence number the tracker assigned to this entry.</param>
/// <param name="Description">Human-readable description of the apply.</param>
/// <param name="AffectedFiles">The file set the apply touched.</param>
/// <param name="ToolName">The originating MCP tool name (e.g., <c>rename_apply</c>).</param>
/// <param name="Capture">The undo capture owned by the recording operation, or <see cref="UndoCaptureId.None"/>.</param>
public sealed record ChangeRecordedEventArgs(
    string WorkspaceId,
    int SequenceNumber,
    string Description,
    IReadOnlyList<string> AffectedFiles,
    string ToolName,
    UndoCaptureId Capture);
