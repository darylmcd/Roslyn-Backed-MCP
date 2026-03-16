using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class MultiFileEditTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "apply_multi_file_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply text edits to multiple files in the workspace sequentially. Each file's edits are applied independently.")]
    public static Task<string> ApplyMultiFileEdit(
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of file edits. Each has filePath (string) and edits (array of TextEditDto with startLine, startColumn, endLine, endColumn, newText)")] FileEditsDto[] fileEdits,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = new List<FileEditSummaryDto>();
                foreach (var fileEdit in fileEdits)
                {
                    var result = await editService.ApplyTextEditsAsync(workspaceId, fileEdit.FilePath, fileEdit.Edits, c);
                    var diff = result.Changes.Count > 0 ? string.Join("\n", result.Changes.Select(ch => ch.UnifiedDiff)) : null;
                    results.Add(new FileEditSummaryDto(fileEdit.FilePath, result.EditsApplied, diff));
                }
                var dto = new MultiFileEditResultDto(true, results.Count, results);
                return JsonSerializer.Serialize(dto, JsonOptions);
            }, ct));
    }
}
