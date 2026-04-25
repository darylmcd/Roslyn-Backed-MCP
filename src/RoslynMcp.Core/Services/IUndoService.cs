namespace RoslynMcp.Core.Services;

/// <summary>
/// Records pre-apply workspace state so that prior apply operations can be reverted.
/// </summary>
/// <remarks>
/// Two revert pathways are exposed:
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="RevertAsync"/> — LIFO revert of the most-recently-captured pending snapshot.
/// Compatible with the original <c>revert_last_apply</c> contract.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="RevertBySequenceAsync"/> — late-session revert of an earlier apply identified
/// by the <see cref="WorkspaceChangeDto.SequenceNumber"/> emitted by <c>workspace_changes</c>.
/// Snapshots are committed to per-workspace history when <see cref="IChangeTracker"/>
/// records the corresponding apply, so the LIFO and by-sequence pathways agree on order.
/// </description>
/// </item>
/// </list>
/// </remarks>
public interface IUndoService
{
    /// <summary>
    /// Captures the current solution state before an apply operation.
    /// Call this immediately before mutating the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace being mutated.</param>
    /// <param name="description">Human-readable description of the operation being applied.</param>
    /// <param name="preApplySolution">
    /// The solution snapshot before mutation. May be <see langword="null"/> for direct-apply
    /// paths that mutate files outside the Roslyn workspace model (e.g., .editorconfig writes).
    /// </param>
    /// <param name="fileSnapshots">
    /// Optional explicit list of <c>(filePath, originalText)</c> pairs captured before the
    /// mutation. When populated, <see cref="RevertAsync"/> uses this authoritative list to
    /// restore disk content directly — bypassing the fragile "did the Roslyn <c>Solution</c>
    /// diff detect a change?" path that fails when <c>MSBuildWorkspace.TryApplyChanges</c>
    /// silently no-ops (FLAG-9A). Direct-file callers such as <c>EditService</c> and
    /// <c>EditorConfigService</c> should always populate this.
    /// </param>
    void CaptureBeforeApply(
        string workspaceId,
        string description,
        object? preApplySolution,
        IReadOnlyList<FileSnapshotDto>? fileSnapshots = null);

    /// <summary>
    /// Returns metadata about the last undoable operation for the given workspace,
    /// or <see langword="null"/> if nothing is undoable.
    /// </summary>
    UndoEntry? GetLastOperation(string workspaceId);

    /// <summary>
    /// Reverts the most recent apply operation for the given workspace.
    /// Returns <see langword="true"/> if the revert succeeded.
    /// </summary>
    Task<bool> RevertAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts the apply operation identified by the given sequence number — the same
    /// sequence number reported by <c>workspace_changes</c>.
    /// </summary>
    /// <remarks>
    /// Fails with <see cref="RevertBySequenceResult.Reason"/> = <c>"dependency-blocked"</c>
    /// when any later apply on the same workspace touches files that overlap with the
    /// target apply's affected-file set — reverting the target would silently roll back
    /// part of those later applies. Conservative file-overlap detection is used: if the
    /// later apply's affected-file set intersects the target's, the revert is blocked.
    /// </remarks>
    /// <param name="workspaceId">The workspace whose history is being inspected.</param>
    /// <param name="sequenceNumber">The target apply's sequence number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RevertBySequenceResult> RevertBySequenceAsync(
        string workspaceId,
        int sequenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the most recently captured pending snapshot to the workspace's revert history,
    /// associating it with the sequence number assigned by <see cref="IChangeTracker"/>. The
    /// caller is <see cref="IChangeTracker"/> itself via the <see cref="IChangeTracker.ChangeRecorded"/>
    /// event — services that apply edits do not call this directly. Idempotent when called with
    /// no pending snapshot (e.g., when the apply path skipped <see cref="CaptureBeforeApply"/>).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="sequenceNumber">The sequence number assigned by <see cref="IChangeTracker"/>.</param>
    /// <param name="affectedFiles">The file set recorded by <see cref="IChangeTracker"/>.</param>
    void CommitPendingCapture(string workspaceId, int sequenceNumber, IReadOnlyList<string> affectedFiles);

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

/// <summary>
/// An explicit pre-apply file snapshot passed to <see cref="IUndoService.CaptureBeforeApply"/>
/// by direct-file callers. Used by <see cref="IUndoService.RevertAsync"/> to restore disk
/// content even when the workspace <c>Solution</c> diff is empty (FLAG-9A).
/// </summary>
/// <param name="FilePath">The absolute path of the file about to be modified.</param>
/// <param name="OriginalText">The full pre-mutation text of the file (null if it didn't exist yet).</param>
public sealed record FileSnapshotDto(string FilePath, string? OriginalText);

/// <summary>
/// Result of <see cref="IUndoService.RevertBySequenceAsync"/>.
/// </summary>
/// <param name="Reverted">Whether the target apply was successfully reverted.</param>
/// <param name="RevertedOperation">
/// Human-readable description of the reverted operation, or <see langword="null"/> when the
/// revert failed (e.g., the sequence number is unknown or the dependency check blocked it).
/// </param>
/// <param name="AffectedFiles">
/// The file set the target apply touched, as recorded by <see cref="IChangeTracker"/>.
/// Empty when the revert failed before the target was located.
/// </param>
/// <param name="Reason">
/// Machine-readable failure code when <see cref="Reverted"/> is <see langword="false"/>:
/// <list type="bullet">
/// <item><description><c>"unknown-sequence"</c> — no snapshot exists for that sequence number.</description></item>
/// <item><description><c>"dependency-blocked"</c> — at least one later apply touches an overlapping file.</description></item>
/// <item><description><c>"revert-failed"</c> — the snapshot existed but the revert mechanics failed (e.g., disk write rejected).</description></item>
/// </list>
/// <see langword="null"/> when <see cref="Reverted"/> is <see langword="true"/>.
/// </param>
/// <param name="BlockingSequences">
/// Populated when <see cref="Reason"/> is <c>"dependency-blocked"</c>: the sequence numbers of
/// later applies whose affected-file sets overlap with the target. <see langword="null"/> in all
/// other cases.
/// </param>
public sealed record RevertBySequenceResult(
    bool Reverted,
    string? RevertedOperation,
    IReadOnlyList<string> AffectedFiles,
    string? Reason,
    IReadOnlyList<int>? BlockingSequences);
