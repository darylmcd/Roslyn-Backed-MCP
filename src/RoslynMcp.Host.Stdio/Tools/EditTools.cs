using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class EditTools
{

    [McpServerTool(Name = "apply_text_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("⚠ DISK-DIRECT, NOT REVERTIBLE (UX-004): Apply one or more text edits to a source file in the workspace. Each edit specifies a range (start/end line and column) and the replacement text. The workspace is updated in-place and a diff is returned. Unlike the Roslyn preview/apply tools (rename_*, extract_*, code_fix_*, etc.), edits made through this tool are NOT registered with revert_last_apply — once applied they can only be undone via git/file-system restore. Prefer a preview/apply Roslyn tool whenever one exists for the change you need; only fall back to apply_text_edit when no semantic equivalent is available.")]
    public static Task<string> ApplyTextEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to edit")] string filePath,
        [Description("Array of text edits. Each edit has startLine, startColumn, endLine, endColumn (1-based), and newText")] TextEditDto[] edits,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunWriteAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await editService.ApplyTextEditsAsync(workspaceId, filePath, edits, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
