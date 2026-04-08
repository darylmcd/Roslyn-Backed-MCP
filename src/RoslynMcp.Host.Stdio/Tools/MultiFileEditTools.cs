using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class MultiFileEditTools
{

    [McpServerTool(Name = "apply_multi_file_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply text edits to multiple files in the workspace sequentially. All edits share a single pre-apply snapshot — revert_last_apply rolls back the entire batch atomically. If a file validation or apply fails partway through, revert_last_apply restores the pre-call state for any files that were already written.")]
    public static Task<string> ApplyMultiFileEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of file edits. Each has filePath (string) and edits (array of TextEditDto with startLine, startColumn, endLine, endColumn, newText)")] FileEditsDto[] fileEdits,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("apply_multi_file_edit", () =>
            gate.RunWriteAsync(workspaceId, async c =>
            {
                // Validate ALL paths before snapshotting so a bad path does not leave a stale undo entry.
                foreach (var fileEdit in fileEdits)
                {
                    await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, fileEdit.FilePath, c).ConfigureAwait(false);
                }

                var dto = await editService.ApplyMultiFileTextEditsAsync(workspaceId, fileEdits, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
            }, ct));
    }
}
