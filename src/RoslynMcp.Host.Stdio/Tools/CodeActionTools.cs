using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CodeActionTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_code_actions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get available Roslyn code fixes and refactorings at a position or selection range in a source file")]
    public static Task<string> GetCodeActions(
        IWorkspaceExecutionGate gate,
        ICodeActionService codeActionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line number")] int startLine,
        [Description("1-based start column number")] int startColumn,
        [Description("Optional: 1-based end line number for a selection range")] int? endLine = null,
        [Description("Optional: 1-based end column number for a selection range")] int? endColumn = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await codeActionService.GetCodeActionsAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, c);
                return JsonSerializer.Serialize(new { count = results.Count, actions = results }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "preview_code_action", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview the changes that a specific code action would make. Use get_code_actions first to get the available actions and their indices.")]
    public static Task<string> PreviewCodeAction(
        IWorkspaceExecutionGate gate,
        ICodeActionService codeActionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line number")] int startLine,
        [Description("1-based start column number")] int startColumn,
        [Description("The index of the code action to preview (from get_code_actions result)")] int actionIndex,
        [Description("Optional: 1-based end line number for a selection range")] int? endLine = null,
        [Description("Optional: 1-based end column number for a selection range")] int? endColumn = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await codeActionService.PreviewCodeActionAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, actionIndex, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code action using its preview token")]
    public static Task<string> ApplyCodeAction(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The preview token returned by preview_code_action")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.ApplyGateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}
