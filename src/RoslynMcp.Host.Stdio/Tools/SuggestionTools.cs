using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SuggestionTools
{
    [McpServerTool(Name = "suggest_refactorings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpToolMetadata("advanced-analysis", "stable", true, false,
        "Analyze the workspace and return ranked refactoring suggestions based on complexity, cohesion (LCOM4), and unused symbol detection. Each suggestion includes severity, target, and recommended tool sequence.")]
    [Description("Analyze the workspace and return ranked refactoring suggestions based on complexity metrics, cohesion analysis (LCOM4), and unused symbol detection. Each suggestion includes severity, description, target symbol location, and a recommended tool sequence.")]
    public static Task<string> SuggestRefactorings(
        IWorkspaceExecutionGate gate,
        IRefactoringSuggestionService suggestionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Maximum number of suggestions to return (default: 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var suggestions = await suggestionService.SuggestRefactoringsAsync(
                workspaceId, projectName, limit, c);
            return JsonSerializer.Serialize(new { count = suggestions.Count, suggestions }, JsonDefaults.Indented);
        }, ct);
    }
}
