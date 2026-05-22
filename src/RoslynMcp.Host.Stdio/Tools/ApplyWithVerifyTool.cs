using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// apply-with-verify-and-rollback: atomic apply → compile_check → revert primitive. Wraps
/// the existing <see cref="IRefactoringService.ApplyRefactoringAsync"/> + <c>compile_check</c>
/// + <c>revert_last_apply</c> chain so callers get one tool call instead of three.
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
        IRefactoringService refactoringService,
        ICompileCheckService compileCheckService,
        IUndoService undoService,
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
            // apply-with-verify-diff-not-counts: snapshot pre-apply error IDENTITIES (id+file+line)
            // so we can tell NEW errors from pre-existing ones. Identity-diff replaces the prior
            // count-delta + message-fingerprint heuristic that produced ~14% false-positive
            // rollbacks (5/36 over 14 days) when a pre-existing diagnostic flipped severity class
            // or had its message text shift on the post-apply build path. Shared with
            // EditService's verify=true path so both verify entry points subtract pre-existing
            // errors uniformly. See DiagnosticIdentitySet for the rationale and format.
            var preBaseline = await compileCheckService.CheckAsync(
                workspaceId, new CompileCheckOptions(), c).ConfigureAwait(false);
            var preErrors = DiagnosticIdentitySet.ExtractErrorIdentities(preBaseline);

            // Apply
            var applyResult = await refactoringService.ApplyRefactoringAsync(previewToken, "apply_with_verify", c).ConfigureAwait(false);
            if (!applyResult.Success)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "apply_failed",
                    error = applyResult.Error,
                    appliedFiles = Array.Empty<string>(),
                }, JsonDefaults.Indented);
            }

            // Verify — extract post-apply error identities and subtract the pre-apply set.
            // The remaining identities are "introduced" errors that did not exist at any
            // (id+file+line) location before the apply. Pre-existing errors whose severity
            // flipped, message changed, or column shifted no longer trigger rollback.
            var postCheck = await compileCheckService.CheckAsync(
                workspaceId, new CompileCheckOptions(), c).ConfigureAwait(false);
            var postErrors = DiagnosticIdentitySet.ExtractErrorIdentities(postCheck);

            // Project the introduced identities back to the diagnostic rows so the response
            // surfaces the actual errors (id, message, location) rather than opaque
            // identity strings. Use the post-apply diagnostic list as the source of truth
            // for the introduced rows.
            var newIdentities = new HashSet<string>(postErrors.Except(preErrors), StringComparer.Ordinal);
            var newErrors = postCheck.Diagnostics
                .Where(d => string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)
                    && newIdentities.Contains(DiagnosticIdentitySet.FormatIdentity(d)))
                .ToList();

            if (newErrors.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "applied",
                    appliedFiles = applyResult.AppliedFiles,
                    preErrorCount = preBaseline.ErrorCount,
                    postErrorCount = postCheck.ErrorCount,
                }, JsonDefaults.Indented);
            }

            // New errors appeared. Either roll back or surface for inspection.
            if (!rollbackOnError)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "applied_with_errors",
                    appliedFiles = applyResult.AppliedFiles,
                    introducedErrors = newErrors,
                    preErrorCount = preBaseline.ErrorCount,
                    postErrorCount = postCheck.ErrorCount,
                    message = "Apply introduced new compile errors; rollbackOnError was false so the broken state is preserved for inspection. Call revert_last_apply to restore.",
                }, JsonDefaults.Indented);
            }

            var reverted = await undoService.RevertAsync(workspaceId, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                status = reverted ? "rolled_back" : "rollback_failed",
                appliedFiles = applyResult.AppliedFiles,
                introducedErrors = newErrors,
                preErrorCount = preBaseline.ErrorCount,
                postErrorCount = postCheck.ErrorCount,
                message = reverted
                ? "Apply introduced new compile errors and was reverted. The workspace is back to the pre-apply state."
                : "Apply introduced new compile errors AND the rollback also failed — the workspace is in an inconsistent state. Inspect manually.",
            }, JsonDefaults.Indented);
        }, ct);
    }
}
