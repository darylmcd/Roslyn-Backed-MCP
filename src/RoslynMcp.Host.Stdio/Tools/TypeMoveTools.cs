using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class TypeMoveTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "move_type_to_file_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Preview moving a type declaration from a multi-type file into its own dedicated file within the same project")]
    public static Task<string> PreviewMoveTypeToFile(
        IWorkspaceExecutionGate gate,
        ITypeMoveService typeMoveService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string sourceFilePath,
        [Description("Name of the type to move")] string typeName,
        [Description("Optional: target file path. If omitted, defaults to {TypeName}.cs in the same directory")] string? targetFilePath = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await typeMoveService.PreviewMoveTypeToFileAsync(workspaceId, sourceFilePath, typeName, targetFilePath, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "move_type_to_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed move-type-to-file refactoring")]
    public static Task<string> ApplyMoveTypeToFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by move_type_to_file_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}
