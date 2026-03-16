using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class EditTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "apply_text_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply one or more text edits to a source file in the workspace. Each edit specifies a range (start/end line and column) and the replacement text. The workspace is updated in-place and a diff is returned.")]
    public static Task<string> ApplyTextEdit(
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to edit")] string filePath,
        [Description("Array of text edits. Each edit has startLine, startColumn, endLine, endColumn (1-based), and newText")] TextEditDto[] edits,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await editService.ApplyTextEditsAsync(workspaceId, filePath, edits, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}
