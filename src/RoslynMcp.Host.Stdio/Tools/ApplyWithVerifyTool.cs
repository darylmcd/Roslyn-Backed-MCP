using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// apply-with-verify-and-rollback: atomic apply → compile_check → revert primitive. Wraps
/// the existing <see cref="IRefactoringService.ApplyRefactoringAsync"/> + <c>compile_check</c>
/// + <c>revert_last_apply</c> chain so callers get one tool call instead of three. The apply →
/// verify → diff → rollback decision logic lives in <see cref="IApplyUndoWorkflowService"/>
/// (Roslyn layer); this wrapper only resolves the workspace, opens the write gate, and maps the
/// returned <see cref="ApplyVerifyOutcome"/> onto the tool's JSON wire shape.
/// </summary>
[McpServerToolType]
public static class ApplyWithVerifyTool
{
    [McpServerTool(Name = "apply_with_verify", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "experimental", false, true,
        "Apply a preview AND immediately verify via compile_check; auto-revert on new errors."),
     Description("Apply a previously previewed refactoring AND immediately verify the workspace still compiles. When new compile errors appear (relative to the pre-apply baseline), automatically revert via revert_last_apply and return status=\"rolled_back\" with the introduced errors. Otherwise return status=\"applied\". Pass rollbackOnError=false to keep broken state for inspection (returns status=\"applied_with_errors\").")]
    public static Task<string> ApplyWithVerify(
        IWorkspaceExecutionGate gate,
        IApplyUndoWorkflowService workflowService,
        IPreviewStore previewStore,
        [Description("The preview token returned by an *_preview tool")] string previewToken,
        [Description("If true (default) and the apply introduces new compile errors, automatically revert via revert_last_apply.")] bool rollbackOnError = true,
        CancellationToken ct = default)
    {
        var workspaceId = previewStore.PeekWorkspaceId(previewToken)
            ?? throw new PreviewTokenStaleException(
                previewToken,
                $"Preview token '{previewToken}' has expired or was invalidated: the workspace was reloaded after the preview was created, dropping the stored solution snapshot. Re-issue the paired *_preview call against the current workspace.");

        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var outcome = await workflowService.ApplyWithVerifyAsync(previewToken, rollbackOnError, c).ConfigureAwait(false);

            return outcome switch
            {
                ApplyVerifyOutcome.ApplyFailed applyFailed => JsonSerializer.Serialize(new
                {
                    status = "apply_failed",
                    error = applyFailed.Error,
                    appliedFiles = Array.Empty<string>(),
                }, JsonDefaults.Indented),

                ApplyVerifyOutcome.Applied applied => JsonSerializer.Serialize(new
                {
                    status = "applied",
                    appliedFiles = applied.AppliedFiles,
                    preErrorCount = applied.PreErrorCount,
                    postErrorCount = applied.PostErrorCount,
                }, JsonDefaults.Indented),

                ApplyVerifyOutcome.AppliedWithErrors appliedWithErrors => JsonSerializer.Serialize(new
                {
                    status = "applied_with_errors",
                    appliedFiles = appliedWithErrors.AppliedFiles,
                    introducedErrors = appliedWithErrors.IntroducedErrors,
                    preErrorCount = appliedWithErrors.PreErrorCount,
                    postErrorCount = appliedWithErrors.PostErrorCount,
                    message = "Apply introduced new compile errors; rollbackOnError was false so the broken state is preserved for inspection. Call revert_last_apply to restore.",
                }, JsonDefaults.Indented),

                ApplyVerifyOutcome.RolledBack rolledBack => JsonSerializer.Serialize(new
                {
                    status = rolledBack.Reverted ? "rolled_back" : "rollback_failed",
                    appliedFiles = rolledBack.AppliedFiles,
                    introducedErrors = rolledBack.IntroducedErrors,
                    preErrorCount = rolledBack.PreErrorCount,
                    postErrorCount = rolledBack.PostErrorCount,
                    message = rolledBack.Reverted
                    ? "Apply introduced new compile errors and was reverted. The workspace is back to the pre-apply state."
                    : "Apply introduced new compile errors AND the rollback also failed — the workspace is in an inconsistent state. Inspect manually.",
                }, JsonDefaults.Indented),

                _ => throw new InvalidOperationException($"Unhandled apply-verify outcome: {outcome.GetType().Name}"),
            };
        }, ct);
    }
}
