using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Host-supplied admission of a composite redemption's write set. Invoked by
/// <see cref="ICompositeApplyOrchestrator.ApplyCompositeAsync"/> on the entry it has just
/// <b>claimed</b> — the exact mutations it is about to write — and before undo capture or the first
/// write, so the decision covers the record that will be applied rather than an earlier peek of a
/// record that a shared persist directory may have swapped since. Throw to refuse; the orchestrator
/// writes nothing.
/// </summary>
/// <param name="claimedWorkspaceId">The workspace id stored in the claimed record.</param>
/// <param name="writeSet">The file path of every mutation in the claimed record.</param>
/// <param name="ct">Cancellation token.</param>
public delegate Task CompositeWriteSetAdmission(
    string claimedWorkspaceId,
    IReadOnlyList<string> writeSet,
    CancellationToken ct);

/// <summary>
/// Applies a composite orchestration preview to disk.
/// </summary>
public interface ICompositeApplyOrchestrator
{
    /// <param name="previewToken">The token returned by the paired preview call.</param>
    /// <param name="admitWriteSet">
    /// Required: the caller's admission of the claimed write set (see
    /// <see cref="CompositeWriteSetAdmission"/>). There is deliberately no overload without it, so no
    /// redemption path can skip the check.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApplyResultDto> ApplyCompositeAsync(
        string previewToken,
        CompositeWriteSetAdmission admitWriteSet,
        CancellationToken ct);
}
