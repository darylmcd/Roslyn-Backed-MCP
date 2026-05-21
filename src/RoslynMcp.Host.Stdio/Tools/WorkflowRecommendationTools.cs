using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkflowRecommendationTools
{
    [McpServerTool(Name = "recommend_workflow", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("orchestration", "experimental", true, false,
        "Recommend first-hop Roslyn MCP tools for a natural-language task."),
     Description("Recommend a compact first-hop workflow for a task. Returns primaryTools, followUpTools, avoid, why, and requiredWorkspaceState so agents can choose focused Roslyn tools without reading the full catalog.")]
    public static Task<string> RecommendWorkflow(
        [Description("Natural-language task, e.g. 'find callers', 'outline this file', 'compile sanity', 'run related tests', or 'rename this symbol'.")] string task,
        [Description("Optional absolute file path relevant to the task.")] string? filePath = null,
        [Description("Optional symbol name, metadata name, or handle relevant to the task.")] string? symbol = null,
        [Description("Optional explicit intent label when the task text is ambiguous.")] string? intent = null)
    {
        if (string.IsNullOrWhiteSpace(task) && string.IsNullOrWhiteSpace(intent))
        {
            throw new ArgumentException("task or intent must be provided.", nameof(task));
        }

        var recommendation = Recommend(task, filePath, symbol, intent);
        return Task.FromResult(JsonSerializer.Serialize(recommendation, JsonDefaults.Indented));
    }

    internal static WorkflowRecommendationDto Recommend(
        string? task,
        string? filePath,
        string? symbol,
        string? intent)
    {
        var text = string.Join(' ', [task, filePath, symbol, intent])
            .ToLowerInvariant();

        if (ContainsAny(text, "caller", "callers", "references", "usages", "used by", "who calls"))
        {
            return new(
                PrimaryTools: ["find_references"],
                FollowUpTools: ["symbol_search", "symbol_info", "find_implementations", "callers_callees"],
                Avoid: ["rg", "grep", "manual file scan"],
                Why: "Use symbol identity instead of text matching so overloads and same-name symbols do not pollute the result.",
                RequiredWorkspaceState: "workspace_loaded");
        }

        if (ContainsAny(text, "outline", "file symbols", "symbols in file", "document symbols", "public surface"))
        {
            return new(
                PrimaryTools: ["document_symbols"],
                FollowUpTools: ["symbol_info", "enclosing_symbol"],
                Avoid: ["reading the entire file top-to-bottom"],
                Why: "document_symbols returns the file's structured symbol outline without spending context on implementation bodies.",
                RequiredWorkspaceState: "workspace_loaded");
        }

        if (ContainsAny(text, "compile", "compilation", "build sanity", "sanity check", "diagnostic sanity"))
        {
            return new(
                PrimaryTools: ["compile_check"],
                FollowUpTools: ["project_diagnostics", "diagnostic_details", "validate_recent_git_changes"],
                Avoid: ["dotnet build", "build_workspace"],
                Why: "compile_check is the fast in-memory first hop for routine compile sanity; reserve full builds for CI-parity validation.",
                RequiredWorkspaceState: "workspace_loaded");
        }

        if (ContainsAny(text, "related test", "related tests", "affected tests", "test this change", "run tests", "tests for file"))
        {
            return new(
                PrimaryTools: ["test_related_files"],
                FollowUpTools: ["test_run --filter", "test_related", "validate_recent_git_changes"],
                Avoid: ["full dotnet test", "Bash dotnet test"],
                Why: "Find the smallest impacted test slice first, then pass its filter to test_run.",
                RequiredWorkspaceState: "workspace_loaded");
        }

        if (ContainsAny(text, "rename", "symbol rename", "refactor rename"))
        {
            return new(
                PrimaryTools: ["rename_preview"],
                FollowUpTools: ["rename_apply", "compile_check", "test_related_files"],
                Avoid: ["manual multi-file edit", "search-and-replace"],
                Why: "The rename preview/apply pair preserves Roslyn symbol identity and call-site updates across the solution.",
                RequiredWorkspaceState: "workspace_loaded");
        }

        return new(
            PrimaryTools: ["discover_capabilities"],
            FollowUpTools: ["server_info", "roslyn://server/catalog"],
            Avoid: ["dumping the full tool catalog before intent is known"],
            Why: "The task does not match a high-confidence first-hop rule; ask for a category-specific capability view before selecting tools.",
            RequiredWorkspaceState: "server_idle_or_ready");
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

internal sealed record WorkflowRecommendationDto(
    IReadOnlyList<string> PrimaryTools,
    IReadOnlyList<string> FollowUpTools,
    IReadOnlyList<string> Avoid,
    string Why,
    string RequiredWorkspaceState);
