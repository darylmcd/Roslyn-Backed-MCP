using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CodeActionTools
{

    [McpServerTool(Name = "get_code_actions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get available Roslyn code fixes and refactorings at a position or selection range in a source file")]
    [McpToolMetadata("code-actions", "stable", true, false,
        "List Roslyn code fixes and refactorings at a location or selection range. Selection-range refactorings include introduce parameter and inline temporary variable. Pass endLine/endColumn for selection-range actions.")]
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
        return ToolErrorHandler.ExecuteAsync("get_code_actions", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await codeActionService.GetCodeActionsAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, c);
                // FLAG-6B: include a hint when the result list is empty so callers understand
                // why nothing was returned (the position may not be on a fixable diagnostic and
                // no refactoring providers may apply to a single-token caret).
                string? hint = null;
                if (results.Count == 0)
                {
                    hint = "No code fixes or refactorings were available at this position. " +
                           "Code fixes only fire when a diagnostic is reported at the span; " +
                           "refactorings typically need a wider selection (e.g. an expression or block) rather than a single caret position. " +
                           "Try widening the range with endLine/endColumn or pointing at a diagnostic flagged by project_diagnostics.";
                }
                return JsonSerializer.Serialize(new { count = results.Count, hint, actions = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "preview_code_action", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview the changes that a specific code action would make. Use get_code_actions first to get the available actions and their indices.")]
    [McpToolMetadata("code-actions", "stable", true, false,
        "Preview a Roslyn code action before applying it.")]
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
        return ToolErrorHandler.ExecuteAsync("preview_code_action", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await codeActionService.PreviewCodeActionAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, actionIndex, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code action using its preview token")]
    [McpToolMetadata("code-actions", "stable", false, true,
        "Apply a previously previewed Roslyn code action.")]
    public static Task<string> ApplyCodeAction(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by preview_code_action")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("apply_code_action", () =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
