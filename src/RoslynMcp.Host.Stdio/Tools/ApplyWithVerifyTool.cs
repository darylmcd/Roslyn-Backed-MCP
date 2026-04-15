using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

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
        return ToolErrorHandler.ExecuteAsync("apply_with_verify", () =>
        {
            var workspaceId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");

            return gate.RunWriteAsync(workspaceId, async c =>
            {
                // Snapshot pre-apply error fingerprints so we can tell NEW errors from pre-existing
                // ones (some repos already have errors; we shouldn't roll back over those).
                var preBaseline = await compileCheckService.CheckAsync(
                    workspaceId, new CompileCheckOptions(), c).ConfigureAwait(false);
                var preErrors = ExtractErrorFingerprints(preBaseline);

                // Apply
                var applyResult = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                if (!applyResult.Success)
                {
                    return JsonSerializer.Serialize(new
                    {
                        status = "apply_failed",
                        error = applyResult.Error,
                        appliedFiles = Array.Empty<string>(),
                    }, JsonDefaults.Indented);
                }

                // Verify
                var postCheck = await compileCheckService.CheckAsync(
                    workspaceId, new CompileCheckOptions(), c).ConfigureAwait(false);
                var postErrors = ExtractErrorFingerprints(postCheck);

                var newErrors = postErrors.Except(preErrors).ToList();
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
        });
    }

    /// <summary>
    /// Builds a stable fingerprint set for compile errors in <paramref name="check"/> so we can
    /// detect NEW errors introduced by the apply (rather than re-flagging pre-existing errors).
    /// Fingerprint = "id|file:line:col|message" — minimizes false matches when the same id
    /// appears at different locations.
    /// </summary>
    private static HashSet<string> ExtractErrorFingerprints(CompileCheckDto check)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in check.Diagnostics)
        {
            if (!string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase)) continue;
            fingerprints.Add($"{d.Id}|{d.FilePath}:{d.StartLine}:{d.StartColumn}|{d.Message}");
        }
        return fingerprints;
    }
}
