using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("undo", "stable", false, true,
        "Revert the most recent Roslyn solution-level apply operation for a workspace."),
     Description("Revert the most recent apply operation for a workspace, restoring the previous solution state. Coverage: renames, code fixes, format, organize usings, apply_text_edit / apply_multi_file_edit, create_file / delete_file / move_file / extract_interface_apply / extract_type_apply / move_type_to_file_apply (Item #2 — 2026-04-16). Project-file (csproj) mutations route through Item #5's SDK-style-safe apply path, which restores the pre-apply csproj bytes as part of the apply itself; on revert the csproj is already in its pre-apply state. Limitation: reverts text edits only — it does NOT remove files created as side effects (e.g. extracted files from extract_type_apply / extract_method_apply / extract_interface_apply). Pair with delete_file_apply on the extracted file to fully undo a file-creating refactor. workspaceId is required.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("workspaceId is required. Pass the session id returned by workspace_load.");
        }
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var entry = undoService.GetLastOperation(workspaceId);
            if (entry is null)
            {
                var nothingResult = new
                {
                    reverted = false,
                    message = "No operation to revert. Nothing has been applied in this session, " +
                              "or the workspace was reloaded / closed and re-loaded since the last apply."
                };
                return JsonSerializer.Serialize(nothingResult, JsonDefaults.Indented);
            }

            var success = await undoService.RevertAsync(workspaceId, c).ConfigureAwait(false);
            if (!success)
            {
                var failResult = new
                {
                    reverted = false,
                    message = "Failed to revert — the workspace state may have changed since the operation was applied.",
                    operation = entry.Description
                };
                return JsonSerializer.Serialize(failResult, JsonDefaults.Indented);
            }

            var result = new
            {
                reverted = true,
                revertedOperation = entry.Description,
                appliedAtUtc = entry.AppliedAtUtc
            };
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
