using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class UndoTools
{
    [McpServerTool(Name = "revert_last_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Revert the most recent apply operation for a workspace, restoring the previous solution state. Only Roslyn solution-level changes (renames, code fixes, format, organize usings) can be reverted. Disk-direct operations (file create/delete/move, project mutations, composite orchestrations) are not revertible.")]
    public static Task<string> RevertLastApply(
        IWorkspaceExecutionGate gate,
        IUndoService undoService,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, _ =>
            {
                var entry = undoService.GetLastOperation(workspaceId);
                if (entry is null)
                    throw new InvalidOperationException("No undoable operation found for this workspace.");

                var success = undoService.Revert(workspaceId, workspace);
                if (!success)
                    throw new InvalidOperationException("Failed to revert — the workspace state may have changed since the operation was applied.");

                var result = new
                {
                    success = true,
                    revertedOperation = entry.Description,
                    appliedAtUtc = entry.AppliedAtUtc
                };
                return Task.FromResult(JsonSerializer.Serialize(result, JsonDefaults.Indented));
            }, ct));
    }
}
