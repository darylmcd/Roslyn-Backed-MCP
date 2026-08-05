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
    private readonly IRefactoringService _refactoringService;
    private readonly ICompileCheckService _compileCheckService;
    private readonly IUndoService _undoService;
    private readonly IPreviewStore _previewStore;
    private readonly TimeSpan _revertBudget;
    private readonly ILogger? _logger;

    public ApplyUndoWorkflowService(
        IRefactoringService refactoringService,
        ICompileCheckService compileCheckService,
        IUndoService undoService,
        IPreviewStore previewStore,
        ILoggerFactory? loggerFactory = null,
        ValidationServiceOptions? options = null)
    {
        _refactoringService = refactoringService;
        _compileCheckService = compileCheckService;
        _undoService = undoService;
        _previewStore = previewStore;
        var configuredRevertBudget = options?.ApplyRevertTimeout ?? TimeSpan.FromSeconds(30);
        _revertBudget = configuredRevertBudget > TimeSpan.Zero
            ? configuredRevertBudget
            : TimeSpan.FromSeconds(30);
        _logger = loggerFactory?.CreateLogger(typeof(ApplyUndoWorkflowService).FullName ?? nameof(ApplyUndoWorkflowService));
    }

    /// <summary>
    /// apply-with-verify-cancelled-result-compensation: best-effort revert issued when a
    /// cancellation is observed after a successful apply, on a fresh configuration-bounded
    /// token (the caller's own token is already cancelled and cannot drive the rollback). A revert
    /// failure — either a <see langword="false"/> result or a thrown exception — is logged at Error
    /// and never swallowed (Directive #3), and never masks the caller's cancellation, which the
    /// caller rethrows after this returns. Shared by both the thrown-OperationCanceledException catch
    /// path and the returned-Cancelled=true DTO check below so both cancellation-observation points
    /// revert identically.
    /// </summary>
    private async Task BestEffortRevertAfterCancellationAsync(string workspaceId)
    {
        using var revertCts = new CancellationTokenSource(_revertBudget);
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

        var projectFilter = ResolveProjectFilter(previewToken);
        var preBaseline = await CapturePreApplyBaselineAsync(
            workspaceId,
            projectFilter,
            ct).ConfigureAwait(false);
        var applyResult = await _refactoringService.ApplyRefactoringAsync(previewToken, "apply_with_verify", ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return new ApplyVerifyOutcome.ApplyFailed(applyResult.Error);
        }

        return await VerifyAppliedEditAsync(
            workspaceId,
            projectFilter,
            preBaseline,
            applyResult,
            rollbackOnError,
            ct).ConfigureAwait(false);
    }

    private string? ResolveProjectFilter(string previewToken)
    {
        if (_previewStore.Retrieve(previewToken) is not { } previewEntry)
        {
            return null;
        }

        var changedProjects = previewEntry.ModifiedSolution
            .GetChanges(previewEntry.OriginalSolution)
            .GetProjectChanges()
            .Select(projectChange => projectChange.NewProject.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return changedProjects.Count == 1 ? changedProjects[0] : null;
    }

    private async Task<CompilationErrorSnapshot> CapturePreApplyBaselineAsync(
        string workspaceId,
        string? projectFilter,
        CancellationToken ct)
    {
        var baseline = await CompilationVerification.CaptureAsync(
            _compileCheckService,
            workspaceId,
            projectFilter,
            ct).ConfigureAwait(false);
        if (!baseline.Cancelled)
        {
            return baseline;
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException(ct);
    }

    private async Task<ApplyVerifyOutcome> VerifyAppliedEditAsync(
        string workspaceId,
        string? projectFilter,
        CompilationErrorSnapshot preBaseline,
        ApplyResultDto applyResult,
        bool rollbackOnError,
        CancellationToken ct)
    {
        var revertedForCancelledDto = false;
        try
        {
            var postCheck = await CompilationVerification.CaptureAsync(
                _compileCheckService,
                workspaceId,
                projectFilter,
                ct).ConfigureAwait(false);
            if (postCheck.Cancelled)
            {
                await BestEffortRevertAfterCancellationAsync(workspaceId).ConfigureAwait(false);
                revertedForCancelledDto = true;
                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }

            var newErrors = CompilationVerification.FindIntroducedDiagnostics(
                preBaseline,
                postCheck);

            if (newErrors.Count == 0)
            {
                return new ApplyVerifyOutcome.Applied(
                    applyResult.AppliedFiles, preBaseline.ErrorCount, postCheck.ErrorCount);
            }

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
