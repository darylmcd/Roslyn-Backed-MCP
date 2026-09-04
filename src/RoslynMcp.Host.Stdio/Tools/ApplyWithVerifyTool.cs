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
/// <remarks>
/// <b>preview-token-apply-route-provenance:</b> this route is deliberately producer-AGNOSTIC and
/// therefore does NOT call <c>ToolDispatch.RequireCompatibleProducer</c>. Its supported set is
/// "every token held by <see cref="IPreviewStore"/>" — the store is the parameter type, so the
/// admissible set is enforced by construction and any <see cref="RoslynMcp.Core.Models.PreviewKind"/>
/// is legitimate here; adding a guard that accepts every member would be unreachable code. The
/// named <c>*_apply</c> routes in <c>RefactoringTools</c> are the ones that bind to a single
/// producer family. Tokens from <c>ICompositePreviewStore</c> / <c>IProjectMutationPreviewStore</c>
/// are structurally out of reach and already surface as <see cref="PreviewTokenStaleException"/>
/// from the <c>PeekWorkspaceId</c> miss below. The tool's <c>[Description]</c> states this set
/// explicitly so callers do not have to infer it.
/// </remarks>
[McpServerToolType]
public static class ApplyWithVerifyTool
{
    [McpServerTool(Name = "apply_with_verify", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "experimental", false, true,
        "Apply a preview AND immediately verify via compile_check; auto-revert on new errors."),
     Description("Apply a solution-snapshot preview token, run compile_check, and auto-revert compile errors. Producer-specific apply routes enforce token-family checks; excludes composite and project-mutation tokens.")]
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
