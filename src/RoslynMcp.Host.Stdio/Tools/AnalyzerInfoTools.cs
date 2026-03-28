using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AnalyzerInfoTools
{
    [McpServerTool(Name = "list_analyzers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List all loaded Roslyn analyzers and their supported diagnostic rules. Use to understand what analysis coverage exists and which diagnostic IDs are available for code_fix_preview or fix_all_preview.")]
    public static Task<string> ListAnalyzers(
        IWorkspaceExecutionGate gate,
        IAnalyzerInfoService analyzerInfoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analyzerInfoService.ListAnalyzersAsync(workspaceId, project, c);
                var totalRules = results.Sum(a => a.Rules.Count);
                return JsonSerializer.Serialize(new { analyzerCount = results.Count, totalRules, analyzers = results }, JsonDefaults.Indented);
            }, ct));
    }
}
