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
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await couplingAnalysisService.GetCouplingMetricsAsync(
                workspaceId, projectName, limit, excludeTestProjects, includeInterfaces, c);

            return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonDefaults.Indented);
        }, ct);
    }
}
