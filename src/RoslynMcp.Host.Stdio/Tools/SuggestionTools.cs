using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SuggestionTools
{
    /// <remarks>
    /// Suggestions are ranked across three independent signals — complexity metrics, LCOM4
    /// cohesion analysis, and unused-symbol detection — and each entry carries a severity, a
    /// human-readable description, the target symbol's location, and the recommended tool
    /// sequence to act on it. Use <c>limit</c> to cap the returned set and <c>projectName</c>
    /// to scope the sweep to a single project.
    /// </remarks>
    [McpServerTool(Name = "suggest_refactorings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpToolMetadata("advanced-analysis", "stable", true, false,
        "Analyze the workspace and return ranked refactoring suggestions based on complexity, cohesion (LCOM4), and unused symbol detection. Each suggestion includes severity, target, and recommended tool sequence.")]
    [Description("Return ranked workspace refactoring suggestions from complexity metrics, cohesion (LCOM4), and unused-symbol detection; each carries severity, target location, and a recommended tool sequence.")]
    public static Task<string> SuggestRefactorings(
        IWorkspaceExecutionGate gate,
        IRefactoringSuggestionService suggestionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Maximum number of suggestions to return (default: 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            async c =>
            {
                var suggestions = await suggestionService.SuggestRefactoringsAsync(
                    workspaceId,
                    projectName,
                    limit,
                    c).ConfigureAwait(false);
                return new { count = suggestions.Count, suggestions };
            },
            ct);
    }
}
