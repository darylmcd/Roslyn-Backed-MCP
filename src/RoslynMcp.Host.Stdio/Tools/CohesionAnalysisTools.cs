using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CohesionAnalysisTools
{

    [McpServerTool(Name = "get_cohesion_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Measure type cohesion via LCOM4 metrics, identifying independent method clusters."),
     Description("Measure type cohesion via LCOM4 metrics. Identifies independent method clusters that share no state — a score > 1 suggests the type has multiple responsibilities and should be split.")]
    public static Task<string> GetCohesionMetrics(
        IWorkspaceExecutionGate gate,
        ICohesionAnalysisService cohesionAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by source file path")] string? filePath = null,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: minimum instance method count threshold (default: 2)")] int? minMethods = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        [Description("When true, include interface types in results and mark them with TypeKind=Interface")] bool includeInterfaces = false,
        [Description("When true, exclude MSBuild test projects (IsTestProject / test framework packages) from analysis")] bool excludeTestProjects = false,
        [Description("When true, exclude test classes (names/paths suggesting tests) from results after metrics are computed")] bool excludeTests = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await cohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath, projectName, minMethods, limit, includeInterfaces, excludeTestProjects, c);

            if (excludeTests)
            {
                results = results.Where(r =>
                    !IsTestTypeName(r.TypeName ?? string.Empty) &&
                    !IsTestFilePath(r.FilePath ?? string.Empty)).ToList();
            }

            return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonDefaults.Indented);
        }, ct);
    }

    private static bool IsTestTypeName(string typeName) =>
        typeName.EndsWith("Tests", StringComparison.Ordinal) ||
        typeName.EndsWith("Test", StringComparison.Ordinal) ||
        typeName.EndsWith("Fixture", StringComparison.Ordinal);

    private static bool IsTestFilePath(string filePath) =>
        filePath.Contains("Tests", StringComparison.OrdinalIgnoreCase) &&
        (filePath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
         filePath.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase));

    [McpServerTool(Name = "find_shared_members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("analysis", "stable", true, false,
        "Find private members used by multiple public methods to inform type extractions."),
     Description("Find private members of a type that are referenced by multiple public methods. Helps plan type extractions by identifying shared dependencies that would need duplication or extraction.")]
    public static Task<string> FindSharedMembers(
        IWorkspaceExecutionGate gate,
        ICohesionAnalysisService cohesionAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var results = await cohesionAnalysisService.FindSharedMembersAsync(workspaceId, locator, c);
            return JsonSerializer.Serialize(new { count = results.Count, sharedMembers = results }, JsonDefaults.Indented);
        }, ct);
    }
}
