using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// apply-undo-workflow-service-extraction: owns the domain decisions of the
/// <c>apply_with_verify</c> and <c>revert_apply_by_sequence</c> tools so the Host-layer
/// wrappers only map the returned outcomes onto their JSON wire shapes. Extracted from
/// <c>ApplyWithVerifyTool</c>/<c>UndoTools</c> so other Roslyn-layer consumers (e.g. the
/// dependent apply/verify workflow consolidation) can reuse the same apply → compile_check →
/// diff → rollback decision path without going through the MCP tool surface.
/// </summary>
public interface IApplyUndoWorkflowService
{
    /// <summary>
    /// Applies a previously previewed refactoring, verifies the workspace still compiles by
    /// comparing pre/post error IDENTITIES (id+file+line), and — when new errors appear and
    /// <paramref name="rollbackOnError"/> is <see langword="true"/> — reverts the apply. Returns
    /// one of the four <see cref="ApplyVerifyOutcome"/> shapes describing what happened. A caller
    /// cancellation observed after a successful apply triggers a bounded best-effort revert on a
    /// fresh token before the <see cref="OperationCanceledException"/> is surfaced.
    /// </summary>
    Task<ApplyVerifyOutcome> ApplyWithVerifyAsync(string previewToken, bool rollbackOnError, CancellationToken ct);

    /// <summary>
    /// Reverts the apply identified by <paramref name="sequenceNumber"/>, surfacing the underlying
    /// <see cref="IUndoService.RevertBySequenceAsync"/> result as a documented
    /// <see cref="SequenceRevertOutcome"/> instead of a raw <c>Reason</c>/<c>BlockingSequences</c>
    /// tuple.
    /// </summary>
    Task<SequenceRevertOutcome> RevertBySequenceAsync(string workspaceId, int sequenceNumber, CancellationToken ct);
}

/// <summary>
/// Outcome of <see cref="IApplyUndoWorkflowService.ApplyWithVerifyAsync"/>. The four variants map
/// 1:1 onto the four distinct JSON shapes the <c>apply_with_verify</c> tool historically produced
/// (<c>apply_failed</c>, <c>applied</c>, <c>applied_with_errors</c>, and the
/// <c>rolled_back</c>/<c>rollback_failed</c> pair collapsed into <see cref="RolledBack"/>).
/// </summary>
public abstract record ApplyVerifyOutcome
{
    private ApplyVerifyOutcome() { }

    /// <summary>The apply itself failed; nothing was mutated (<c>status="apply_failed"</c>).</summary>
    public sealed record ApplyFailed(string? Error) : ApplyVerifyOutcome;

    /// <summary>The apply succeeded and introduced no new compile errors (<c>status="applied"</c>).</summary>
    public sealed record Applied(
        IReadOnlyList<string> AppliedFiles,
        int PreErrorCount,
        int PostErrorCount) : ApplyVerifyOutcome;

    /// <summary>
    /// The apply introduced new errors but <c>rollbackOnError</c> was <see langword="false"/>, so the
    /// broken state is preserved for inspection (<c>status="applied_with_errors"</c>).
    /// </summary>
    public sealed record AppliedWithErrors(
        IReadOnlyList<string> AppliedFiles,
        IReadOnlyList<DiagnosticDto> IntroducedErrors,
        int PreErrorCount,
        int PostErrorCount) : ApplyVerifyOutcome;

    /// <summary>
    /// The apply introduced new errors and a rollback was attempted. <see cref="Reverted"/> is
    /// <see langword="true"/> for <c>status="rolled_back"</c> and <see langword="false"/> for
    /// <c>status="rollback_failed"</c> (the revert itself also failed).
    /// </summary>
    public sealed record RolledBack(
        bool Reverted,
        IReadOnlyList<string> AppliedFiles,
        IReadOnlyList<DiagnosticDto> IntroducedErrors,
        int PreErrorCount,
        int PostErrorCount) : ApplyVerifyOutcome;
}

/// <summary>
/// Outcome of <see cref="IApplyUndoWorkflowService.RevertBySequenceAsync"/> — a documented Roslyn-layer
/// projection of <see cref="RevertBySequenceResult"/> (1:1 fields) so Host wrappers and other consumers
/// read named properties instead of inspecting <c>Reason</c>/<c>BlockingSequences</c> strings from the
/// undo service directly.
/// </summary>
/// <param name="Reverted">Whether the target apply was successfully reverted.</param>
/// <param name="RevertedOperation">Human-readable description of the reverted operation, or <see langword="null"/> on failure.</param>
/// <param name="AffectedFiles">The file set the target apply touched; empty when the revert failed before locating the target.</param>
/// <param name="Reason">Machine-readable failure code (<c>unknown-sequence</c>/<c>dependency-blocked</c>/<c>revert-failed</c>) or <see langword="null"/> on success.</param>
/// <param name="BlockingSequences">Later overlapping sequence numbers when <see cref="Reason"/> is <c>dependency-blocked</c>; otherwise <see langword="null"/>.</param>
public sealed record SequenceRevertOutcome(
    bool Reverted,
    string? RevertedOperation,
    IReadOnlyList<string> AffectedFiles,
    string? Reason,
    IReadOnlyList<int>? BlockingSequences);

/// <inheritdoc cref="IApplyUndoWorkflowService"/>
public sealed class ApplyUndoWorkflowService : IApplyUndoWorkflowService
{
    /// <summary>
    /// apply-with-verify-cancellation-and-compile-scope: budget for the best-effort revert issued
    /// when the caller's <see cref="CancellationToken"/> fires AFTER a successful apply but before
    /// the normal verify/revert leg completes. The original (already-cancelled) token cannot drive
    /// the rollback, so a fresh short-lived token is used so the workspace is not left mutated with
    /// no rollback. Bounded so a wedged revert cannot hang the caller.
    /// </summary>
    private static readonly TimeSpan RevertBudget = TimeSpan.FromSeconds(30);

    private readonly IRefactoringService _refactoringService;
    private readonly ICompileCheckService _compileCheckService;
    private readonly IUndoService _undoService;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger? _logger;

    public ApplyUndoWorkflowService(
        IRefactoringService refactoringService,
        ICompileCheckService compileCheckService,
        IUndoService undoService,
        IPreviewStore previewStore,
        ILoggerFactory? loggerFactory = null)
    {
        _refactoringService = refactoringService;
        _compileCheckService = compileCheckService;
        _undoService = undoService;
        _previewStore = previewStore;
        _logger = loggerFactory?.CreateLogger(typeof(ApplyUndoWorkflowService).FullName ?? nameof(ApplyUndoWorkflowService));
    }

    /// <summary>
    /// apply-with-verify-cancelled-result-compensation: best-effort revert issued when a
    /// cancellation is observed after a successful apply, on a FRESH <see cref="RevertBudget"/>-scoped
    /// token (the caller's own token is already cancelled and cannot drive the rollback). A revert
    /// failure — either a <see langword="false"/> result or a thrown exception — is logged at Error
    /// and never swallowed (Directive #3), and never masks the caller's cancellation, which the
    /// caller rethrows after this returns. Shared by both the thrown-OperationCanceledException catch
    /// path and the returned-Cancelled=true DTO check below so both cancellation-observation points
    /// revert identically.
    /// </summary>
    private async Task BestEffortRevertAfterCancellationAsync(string workspaceId)
    {
        using var revertCts = new CancellationTokenSource(RevertBudget);
        try
        {
            var recovered = await _undoService.RevertAsync(workspaceId, revertCts.Token).ConfigureAwait(false);
            if (!recovered)
            {
                _logger?.LogError(
                    "apply_with_verify: best-effort revert after cancellation reported failure for workspace {WorkspaceId}; the applied edit may still be present.",
                    workspaceId);
            }
        }
        catch (Exception revertEx)
        {
            _logger?.LogError(
                revertEx,
                "apply_with_verify: best-effort revert after cancellation threw for workspace {WorkspaceId}; the applied edit may still be present.",
                workspaceId);
        }
    }

    /// <inheritdoc/>
    public async Task<ApplyVerifyOutcome> ApplyWithVerifyAsync(string previewToken, bool rollbackOnError, CancellationToken ct)
    {
        var workspaceId = _previewStore.PeekWorkspaceId(previewToken)
            ?? throw new PreviewTokenStaleException(
                previewToken,
                $"Preview token '{previewToken}' has expired or was invalidated: the workspace was reloaded after the preview was created, dropping the stored solution snapshot. Re-issue the paired *_preview call against the current workspace.");

        // apply-with-verify-cancellation-and-compile-scope: scope both compile_check legs to the
        // single project the preview actually touches, instead of compiling the FULL solution twice.
        // Mirrors EditService's ownerProjects/batchProjectFilter pattern: exactly one changed
        // project → filter to it; zero or many → null (compile everything, the safe fallback).
        // Retrieve is non-consuming and does not perturb the token's TTL, so the paired apply below
        // still redeems it. A null entry (stale token) falls through to the full-solution compile —
        // correctness-preserving, just slower.
        string? projectFilter = null;
        if (_previewStore.Retrieve(previewToken) is { } previewEntry)
        {
            var changedProjects = previewEntry.ModifiedSolution
                .GetChanges(previewEntry.OriginalSolution)
                .GetProjectChanges()
                .Select(pc => pc.NewProject.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            projectFilter = changedProjects.Count == 1 ? changedProjects[0] : null;
        }

        var checkOptions = new CompileCheckOptions(ProjectFilter: projectFilter);

        // apply-with-verify-diff-not-counts: snapshot pre-apply error IDENTITIES (id+file+line) so we
        // can tell NEW errors from pre-existing ones. Identity-diff replaces the prior count-delta +
        // message-fingerprint heuristic that produced false-positive rollbacks when a pre-existing
        // diagnostic flipped severity class or had its message text shift on the post-apply build
        // path. Shared with EditService's verify=true path so both verify entry points subtract
        // pre-existing errors uniformly. See DiagnosticIdentitySet for the rationale and format.
        var preBaseline = await _compileCheckService.CheckAsync(
            workspaceId, checkOptions, ct).ConfigureAwait(false);

        // apply-with-verify-cancelled-result-compensation: CompileCheckService.CheckAsync catches
        // OperationCanceledException internally and returns a normal (non-throwing) DTO with
        // Cancelled=true instead of propagating the exception — so a caller-token cancellation that
        // landed inside the pre-apply baseline check is otherwise invisible here. Nothing has been
        // applied yet, so surface the cancellation directly with zero apply/revert calls.
        if (preBaseline.Cancelled)
        {
            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException(ct);
        }

        var preErrors = DiagnosticIdentitySet.ExtractErrorIdentities(preBaseline);

        // Apply
        var applyResult = await _refactoringService.ApplyRefactoringAsync(previewToken, "apply_with_verify", ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return new ApplyVerifyOutcome.ApplyFailed(applyResult.Error);
        }

        // apply-with-verify-cancellation-and-compile-scope: the workspace is now mutated. Everything
        // from here to the normal revert leg runs under the caller's token `ct`; if it fires
        // mid-verify (post-check) or mid-revert, an OperationCanceledException would otherwise unwind
        // with the apply left in place and NO rollback. Guard the post-apply region so a cancellation
        // triggers a bounded best-effort revert on a FRESH token before the OCE is rethrown. This
        // catch can only fire when the apply already succeeded (we are past the !Success early
        // return), so a pre-apply cancellation on the baseline compile still propagates untouched.
        //
        // apply-with-verify-cancelled-result-compensation: `revertedForCancelledDto` guards against a
        // DOUBLE revert. The postCheck.Cancelled branch below performs its own best-effort revert and
        // then throws OperationCanceledException from INSIDE this same try block, which the catch
        // immediately below would otherwise also revert for — exactly one best-effort revert is
        // required per cancellation. The flag is false on every path where compileCheckService threw
        // directly (the catch's original responsibility) and true only when this method already
        // reverted before throwing.
        var revertedForCancelledDto = false;
        try
        {
            // Verify — extract post-apply error identities and subtract the pre-apply set. The
            // remaining identities are "introduced" errors that did not exist at any (id+file+line)
            // location before the apply. Pre-existing errors whose severity flipped, message changed,
            // or column shifted no longer trigger rollback.
            var postCheck = await _compileCheckService.CheckAsync(
                workspaceId, checkOptions, ct).ConfigureAwait(false);

            // apply-with-verify-cancelled-result-compensation: as above, a caller-token cancellation
            // that landed inside the post-apply verify check returns a Cancelled=true DTO rather than
            // throwing. The apply already succeeded and mutated the workspace, so — unlike the
            // pre-apply check above — this must attempt exactly one best-effort revert (same
            // fresh-token helper the thrown-OCE catch block below uses) before surfacing the
            // cancellation. `newErrors` would otherwise be computed from a partial diagnostic list.
            if (postCheck.Cancelled)
            {
                await BestEffortRevertAfterCancellationAsync(workspaceId).ConfigureAwait(false);
                revertedForCancelledDto = true;
                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }

            var postErrors = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

            // Project the introduced identities back to the diagnostic rows so the outcome surfaces
            // the actual errors (id, message, location) rather than opaque identity strings. Use the
            // post-apply diagnostic list as the source of truth for the introduced rows.
            var newIdentities = new HashSet<string>(postErrors.Except(preErrors), StringComparer.Ordinal);
            var newErrors = postCheck.Diagnostics
                .Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)
                    && newIdentities.Contains(DiagnosticIdentitySet.FormatIdentity(d)))
                .ToList();

            if (newErrors.Count == 0)
            {
                return new ApplyVerifyOutcome.Applied(
                    applyResult.AppliedFiles, preBaseline.ErrorCount, postCheck.ErrorCount);
            }

            // New errors appeared. Either roll back or surface for inspection.
            if (!rollbackOnError)
            {
                return new ApplyVerifyOutcome.AppliedWithErrors(
                    applyResult.AppliedFiles, newErrors, preBaseline.ErrorCount, postCheck.ErrorCount);
            }

            var reverted = await _undoService.RevertAsync(workspaceId, ct).ConfigureAwait(false);
            return new ApplyVerifyOutcome.RolledBack(
                reverted, applyResult.AppliedFiles, newErrors, preBaseline.ErrorCount, postCheck.ErrorCount);
        }
        catch (OperationCanceledException)
        {
            // The apply landed but the caller cancelled before the verify/revert leg finished. Roll
            // the workspace back best-effort on a fresh token (the caller's token `ct` is already
            // cancelled and cannot drive the revert), then rethrow the original cancellation so the
            // caller still observes it. A second failure in the revert itself is logged rather than
            // swallowed (Directive #3) and does NOT mask the cancellation. Skip the revert here if
            // the postCheck.Cancelled branch above already performed it (revertedForCancelledDto) —
            // otherwise this genuinely is the first (and only) observation of the cancellation.
            if (!revertedForCancelledDto)
            {
                await BestEffortRevertAfterCancellationAsync(workspaceId).ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SequenceRevertOutcome> RevertBySequenceAsync(string workspaceId, int sequenceNumber, CancellationToken ct)
    {
        var result = await _undoService.RevertBySequenceAsync(workspaceId, sequenceNumber, ct).ConfigureAwait(false);
        return new SequenceRevertOutcome(
            result.Reverted,
            result.RevertedOperation,
            result.AffectedFiles,
            result.Reason,
            result.BlockingSequences);
    }
}
