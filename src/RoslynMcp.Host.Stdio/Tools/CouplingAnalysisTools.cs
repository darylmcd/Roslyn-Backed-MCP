using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CouplingAnalysisTools
{
    [McpServerTool(Name = "get_coupling_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Measure per-type afferent/efferent coupling + Martin's instability index."),
     Description("Compute per-type coupling metrics (Martin): AfferentCoupling Ca (incoming refs), EfferentCoupling Ce (outgoing refs), Instability I = Ce / (Ca + Ce). Classifies types as stable / balanced / unstable / isolated. Companion to get_cohesion_metrics — cohesion looks inward, coupling looks outward.")]
    public static Task<string> GetCouplingMetrics(
        IWorkspaceExecutionGate gate,
        ICouplingAnalysisService couplingAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name (case-insensitive exact match)")] string? projectName = null,
        [Description("Maximum number of results to return (default: 100)")] int limit = 100,
        [Description("When true, exclude MSBuild test projects (IsTestProject / test framework packages) from analysis")] bool excludeTestProjects = false,
        [Description("When true, include interface types alongside classes/structs/records")] bool includeInterfaces = false,
        [Description("When true, return per-project rollup counts (typeCount, avgInstability, stableCount, balancedCount, unstableCount, isolatedCount) without per-type detail rows. 10-100x smaller payload on multi-project solutions.")] bool summary = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await couplingAnalysisService.GetCouplingMetricsResultAsync(
                workspaceId, projectName, limit, excludeTestProjects, includeInterfaces, c);
            var results = result.Metrics;

            // Summary mode: per-project rollup with classification buckets, no per-type detail rows.
            // 10-100x smaller payload for multi-project solutions (gh #763).
            if (summary)
            {
                var projectGroups = results
                    .GroupBy(r => r.ProjectName, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        projectName = group.Key,
                        typeCount = group.Count(),
                        avgInstability = group.Average(r => r.Instability),
                        stableCount = group.Count(r => r.Classification == "stable"),
                        balancedCount = group.Count(r => r.Classification == "balanced"),
                        unstableCount = group.Count(r => r.Classification == "unstable"),
                        isolatedCount = group.Count(r => r.Classification == "isolated"),
                    })
                    .OrderBy(g => g.projectName, StringComparer.Ordinal)
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    summary = true,
                    partial = result.IsPartial,
                    failedTypeCount = result.FailedTypeCount,
                    warnings = result.Warnings,
                    projectCount = projectGroups.Count,
                    totalTypes = results.Count,
                    projects = projectGroups,
                }, JsonDefaults.Indented);
            }

            return JsonSerializer.Serialize(new
            {
                count = results.Count,
                partial = result.IsPartial,
                failedTypeCount = result.FailedTypeCount,
                warnings = result.Warnings,
                metrics = results,
            }, JsonDefaults.Indented);
        }, ct);
    }
}
