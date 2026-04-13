using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Revert the most recent apply operation for a workspace, restoring the previous solution state. Roslyn preview/apply operations (renames, code fixes, format, organize usings) AND apply_text_edit / apply_multi_file_edit register for undo. File create/delete/move and project file mutations are not revertible. workspaceId is required.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("revert_last_apply", () =>
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
                                  "or the last operation was an unrevertible change (file create/delete/move " +
                                  "or project file mutation)."
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
        });
    }
}
