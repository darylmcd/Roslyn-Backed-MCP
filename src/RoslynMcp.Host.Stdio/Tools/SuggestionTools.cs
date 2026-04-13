using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SuggestionTools
{
    [McpServerTool(Name = "suggest_refactorings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze the workspace and return ranked refactoring suggestions based on complexity metrics, cohesion analysis (LCOM4), and unused symbol detection. Each suggestion includes severity, description, target symbol location, and a recommended tool sequence.")]
    public static Task<string> SuggestRefactorings(
        IWorkspaceExecutionGate gate,
        IRefactoringSuggestionService suggestionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Maximum number of suggestions to return (default: 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("suggest_refactorings", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var suggestions = await suggestionService.SuggestRefactoringsAsync(
                    workspaceId, projectName, limit, c);
                return JsonSerializer.Serialize(new { count = suggestions.Count, suggestions }, JsonDefaults.Indented);
            }, ct));
    }
}
