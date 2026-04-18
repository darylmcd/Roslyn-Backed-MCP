namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of extending an existing <c>#pragma warning restore &lt;id&gt;</c> further down so
/// the matching disable/restore span covers a previously-uncovered fire site.
/// </summary>
/// <param name="Success">
/// <c>true</c> when the restore directive was relocated (or was already in place) such that
/// <paramref name="TargetLine"/> is inside the disable/restore span and no safety invariants
/// were violated. <c>false</c> when the widen was refused — see <paramref name="Reason"/>.
/// </param>
/// <param name="FilePath">Absolute path of the file that was inspected or modified.</param>
/// <param name="TargetLine">1-based line the caller wanted covered (typically the diagnostic fire site).</param>
/// <param name="DiagnosticId">The diagnostic id whose <c>restore</c> was extended.</param>
/// <param name="DisableLine">1-based line of the matched <c>#pragma warning disable &lt;id&gt;</c>, or <c>null</c> when no pair was located.</param>
/// <param name="OriginalRestoreLine">
/// 1-based line of the <c>#pragma warning restore &lt;id&gt;</c> as it existed before this call,
/// or <c>null</c> when the disable had no matching restore (dangling / file-end).
/// </param>
/// <param name="NewRestoreLine">
/// 1-based line of the <c>#pragma warning restore &lt;id&gt;</c> after this call. Strictly greater
/// than <paramref name="TargetLine"/> on success. Equal to <paramref name="OriginalRestoreLine"/>
/// on the no-op path (the pair already covers the target). <c>null</c> on failure.
/// </param>
/// <param name="AlreadyCovered">
/// <c>true</c> when the existing pair already covered <paramref name="TargetLine"/> — no edit
/// was made and no undo snapshot was captured. The caller can treat this as an idempotent success.
/// </param>
/// <param name="Reason">
/// Human-readable explanation — on success, a short confirmation; on refusal, the specific
/// safety invariant that blocked the widen (e.g. "would cross #region boundary",
/// "would nest into another pragma disable for the same id").
/// </param>
public sealed record PragmaWidenResultDto(
    bool Success,
    string FilePath,
    int TargetLine,
    string DiagnosticId,
    int? DisableLine,
    int? OriginalRestoreLine,
    int? NewRestoreLine,
    bool AlreadyCovered,
    string Reason);
