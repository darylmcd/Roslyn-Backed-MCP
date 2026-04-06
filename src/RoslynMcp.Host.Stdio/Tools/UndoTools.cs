using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Revert the most recent apply operation for a workspace, restoring the previous solution state. Only Roslyn preview/apply operations (renames, code fixes, format, organize usings) register for undo. apply_text_edit and other disk-direct edits are not revertible here. workspaceId is required.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
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
                                  "or the last operation was a disk-direct change (file create/delete/move, " +
                                  "project mutation) which is not revertible."
                    };
                    return JsonSerializer.Serialize(nothingResult, JsonDefaults.Indented);
                }

                var success = await undoService.RevertAsync(workspaceId, workspace, c).ConfigureAwait(false);
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
