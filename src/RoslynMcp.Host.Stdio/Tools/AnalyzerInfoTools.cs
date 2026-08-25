using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AnalyzerInfoTools
{
    /// <remarks>
    /// Response JSON: <c>analyzerCount</c> = total distinct analyzer assemblies (unpaged);
    /// <c>totalRules</c> = sum of rules across all assemblies — use this for the total rule count
    /// without paging the full list; <c>returnedRules</c> = rules in this page;
    /// <c>returnedAnalyzerCount</c> = distinct assemblies that contributed at least one rule in
    /// this page; <c>hasMore</c> = true when <c>offset + returnedRules &lt; totalRules</c>.
    /// </remarks>
    [McpServerTool(Name = "list_analyzers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "List all loaded analyzers and their diagnostic rules."),
     Description("List loaded Roslyn analyzers and their rules — which diagnostic IDs are available for code_fix_preview or fix_all_preview. offset/limit paginate the flattened rule list, not whole analyzers.")]
    public static Task<string> ListAnalyzers(
        IWorkspaceExecutionGate gate,
        IAnalyzerInfoService analyzerInfoService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Number of analyzer rules to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum number of analyzer rules to return (default: 100)")] int limit = 100,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidatePagination(offset, limit);
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await analyzerInfoService.ListAnalyzersAsync(workspaceId, projectName, c);
            var totalRules = results.Sum(a => a.Rules.Count);

            var pagedRules = results
                .SelectMany(analyzer => analyzer.Rules.Select(rule => new { analyzer.AssemblyName, Rule = rule }))
                .Skip(offset)
                .Take(limit)
                .ToList();

            var pagedAnalyzers = pagedRules
                .GroupBy(entry => entry.AssemblyName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new RoslynMcp.Core.Models.AnalyzerInfoDto(
                    group.Key,
                    group.Select(entry => entry.Rule).ToList()))
                .OrderBy(analyzer => analyzer.AssemblyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return JsonSerializer.Serialize(new
            {
                analyzerCount = results.Count,
                totalRules,
                offset,
                limit,
                returnedRules = pagedRules.Count,
                returnedAnalyzerCount = pagedAnalyzers.Count,
                hasMore = offset + pagedRules.Count < totalRules,
                analyzers = pagedAnalyzers,
            }, JsonDefaults.Indented);
        }, ct);
    }
}
