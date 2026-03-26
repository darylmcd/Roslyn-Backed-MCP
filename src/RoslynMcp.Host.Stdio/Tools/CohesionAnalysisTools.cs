using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CohesionAnalysisTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_cohesion_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Measure type cohesion via LCOM4 metrics. Identifies independent method clusters that share no state — a score > 1 suggests the type has multiple responsibilities and should be split.")]
    public static Task<string> GetCohesionMetrics(
        IWorkspaceExecutionGate gate,
        ICohesionAnalysisService cohesionAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by source file path")] string? filePath = null,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: minimum instance method count threshold (default: 2)")] int? minMethods = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await cohesionAnalysisService.GetCohesionMetricsAsync(workspaceId, filePath, project, minMethods, limit, c);
                return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "find_shared_members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find private members of a type that are referenced by multiple public methods. Helps plan type extractions by identifying shared dependencies that would need duplication or extraction.")]
    public static Task<string> FindSharedMembers(
        IWorkspaceExecutionGate gate,
        ICohesionAnalysisService cohesionAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle);
                var results = await cohesionAnalysisService.FindSharedMembersAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(new { count = results.Count, sharedMembers = results }, JsonOptions);
            }, ct));
    }
}
