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
/// What a composite redemption must satisfy before it may write: the session its caller gated on and
/// the admission of the claimed write set.
/// </summary>
/// <param name="GatedWorkspaceId">
/// The local workspace session the caller took its write gate on, or <see langword="null"/> when no
/// gate was taken. A persisted store refuses, before consuming the token, when the record no longer
/// resolves to this session.
/// </param>
/// <param name="AdmitWriteSet">The admission run on the claimed entry (see <see cref="CompositeWriteSetAdmission"/>).</param>
public sealed record CompositeRedemptionAdmission(
    string? GatedWorkspaceId,
    CompositeWriteSetAdmission AdmitWriteSet);

/// <summary>
/// Applies a composite orchestration preview to disk.
/// </summary>
public interface ICompositeApplyOrchestrator
{
    /// <param name="previewToken">The token returned by the paired preview call.</param>
    /// <param name="admission">
    /// Required: the caller's gated session and admission of the claimed write set (see
    /// <see cref="CompositeRedemptionAdmission"/>). There is deliberately no overload without it, so no
    /// redemption path can skip the check.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApplyResultDto> ApplyCompositeAsync(
        string previewToken,
        CompositeRedemptionAdmission admission,
        CancellationToken ct);
}
