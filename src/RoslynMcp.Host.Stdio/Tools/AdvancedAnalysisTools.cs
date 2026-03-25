using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AdvancedAnalysisTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "find_unused_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find symbols (types, methods, properties, fields) with zero references across the solution — helps identify dead code")]
    public static Task<string> FindUnusedSymbols(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Include public symbols in the search (default: false, since public APIs may be consumed externally)")] bool includePublic = false,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analysisService.FindUnusedSymbolsAsync(workspaceId, project, includePublic, limit, c);
                return JsonSerializer.Serialize(new { count = results.Count, unusedSymbols = results }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "get_di_registrations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Scan the solution for dependency injection registrations (AddSingleton, AddScoped, AddTransient) and return the service-to-implementation mappings")]
    public static Task<string> GetDiRegistrations(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analysisService.GetDiRegistrationsAsync(workspaceId, project, c);
                return JsonSerializer.Serialize(new { count = results.Count, registrations = results }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "get_complexity_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Calculate cyclomatic complexity, lines of code, nesting depth, and parameter count for methods in the workspace")]
    public static Task<string> GetComplexityMetrics(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by source file path")] string? filePath = null,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: minimum cyclomatic complexity threshold (default: return all)")] int? minComplexity = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analysisService.GetComplexityMetricsAsync(workspaceId, filePath, project, minComplexity, limit, c);
                return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "find_reflection_usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find all reflection API usage in the solution (typeof, Type.GetMethod, Activator.CreateInstance, Assembly.Load, etc.)")]
    public static Task<string> FindReflectionUsages(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analysisService.FindReflectionUsagesAsync(workspaceId, project, c);
                var grouped = results.GroupBy(r => r.UsageKind)
                    .ToDictionary(g => g.Key, g => g.ToList());
                return JsonSerializer.Serialize(new { count = results.Count, usagesByKind = grouped }, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "get_namespace_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the namespace dependency graph and detect circular namespace dependencies in the solution")]
    public static Task<string> GetNamespaceDependencies(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await analysisService.GetNamespaceDependenciesAsync(workspaceId, project, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "get_nuget_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List all NuGet package references across projects in the workspace, including which projects use each package")]
    public static Task<string> GetNuGetDependencies(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await analysisService.GetNuGetDependenciesAsync(workspaceId, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "semantic_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search for symbols by semantic criteria. Supports natural language queries like 'async methods returning Task<bool>', 'classes implementing IDisposable', 'methods with more than 5 parameters', 'static methods', 'virtual properties', 'generic classes', etc.")]
    public static Task<string> SemanticSearch(
        IWorkspaceExecutionGate gate,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Semantic search query, e.g. 'async methods returning Task<bool>', 'classes implementing IDisposable', 'public static methods'")] string query,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await analysisService.SemanticSearchAsync(workspaceId, query, project, limit, c);
                return JsonSerializer.Serialize(new { count = results.Count, results }, JsonOptions);
            }, ct));
    }
}
