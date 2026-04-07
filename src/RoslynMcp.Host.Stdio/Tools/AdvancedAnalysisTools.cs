using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AdvancedAnalysisTools
{

    [McpServerTool(Name = "find_unused_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find symbols (types, methods, properties, fields) with zero references across the solution — helps identify dead code. Each hit includes Confidence: high (private/internal), medium (public API), low (enum members, record/serialization-shaped properties, interface members — often false positives).")]
    public static Task<string> FindUnusedSymbols(
        IWorkspaceExecutionGate gate,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Include public symbols in the search (default: false, since public APIs may be consumed externally)")] bool includePublic = false,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        [Description("When true, skip enum members (often referenced indirectly).")] bool excludeEnums = false,
        [Description("When true, skip properties declared on record types (often DTO/serialization shaped).")] bool excludeRecordProperties = false,
        [Description("When true, skip projects whose names look like test projects (*.Tests, *Tests).")] bool excludeTestProjects = false,
        [Description("When true, skip symbols in test fixture types (xUnit/NUnit/MSTest-shaped names and attributes).")] bool excludeTests = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await unusedCodeAnalyzer.FindUnusedSymbolsAsync(
                    workspaceId, project, includePublic, limit, excludeEnums, excludeRecordProperties, excludeTestProjects, excludeTests, c);
                return JsonSerializer.Serialize(new { count = results.Count, unusedSymbols = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_di_registrations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Scan the solution for dependency injection registrations (AddSingleton, AddScoped, AddTransient) and return the service-to-implementation mappings")]
    public static Task<string> GetDiRegistrations(
        IWorkspaceExecutionGate gate,
        IDependencyAnalysisService dependencyAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await dependencyAnalysisService.GetDiRegistrationsAsync(workspaceId, project, c);
                return JsonSerializer.Serialize(new { count = results.Count, registrations = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_complexity_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Calculate cyclomatic complexity, lines of code, nesting depth, parameter count, and an approximate maintainability index (0–100, higher is better) for methods in the workspace")]
    public static Task<string> GetComplexityMetrics(
        IWorkspaceExecutionGate gate,
        ICodeMetricsService codeMetricsService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by source file path")] string? filePath = null,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: minimum cyclomatic complexity threshold (default: return all)")] int? minComplexity = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await codeMetricsService.GetComplexityMetricsAsync(workspaceId, filePath, project, minComplexity, limit, c);
                return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_reflection_usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find all reflection API usage in the solution (typeof, Type.GetMethod, Activator.CreateInstance, Assembly.Load, etc.)")]
    public static Task<string> FindReflectionUsages(
        IWorkspaceExecutionGate gate,
        ICodePatternAnalyzer codePatternAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await codePatternAnalyzer.FindReflectionUsagesAsync(workspaceId, project, c);
                var grouped = results.GroupBy(r => r.UsageKind)
                    .ToDictionary(g => g.Key, g => g.ToList());
                return JsonSerializer.Serialize(new { count = results.Count, usagesByKind = grouped }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_namespace_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the namespace dependency graph and detect circular namespace dependencies in the solution")]
    public static Task<string> GetNamespaceDependencies(
        IWorkspaceExecutionGate gate,
        IDependencyAnalysisService dependencyAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("When true, return only namespaces and edges involved in circular dependencies")] bool circularOnly = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await dependencyAnalysisService.GetNamespaceDependenciesAsync(workspaceId, project, c);

                if (circularOnly && result.CircularDependencies.Count > 0)
                {
                    var cyclicNamespaces = new HashSet<string>(
                        result.CircularDependencies.SelectMany(cd => cd.Cycle),
                        StringComparer.Ordinal);

                    result = new RoslynMcp.Core.Models.NamespaceDependencyGraphDto(
                        result.Nodes.Where(n => cyclicNamespaces.Contains(n.Namespace)).ToList(),
                        result.Edges.Where(e => cyclicNamespaces.Contains(e.FromNamespace) &&
                                                cyclicNamespaces.Contains(e.ToNamespace)).ToList(),
                        result.CircularDependencies);
                }
                else if (circularOnly)
                {
                    result = new RoslynMcp.Core.Models.NamespaceDependencyGraphDto([], [], []);
                }

                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_nuget_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List all NuGet package references across projects in the workspace, including which projects use each package")]
    public static Task<string> GetNuGetDependencies(
        IWorkspaceExecutionGate gate,
        IDependencyAnalysisService dependencyAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await dependencyAnalysisService.GetNuGetDependenciesAsync(workspaceId, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "semantic_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search for symbols by semantic criteria. Supports natural language queries like 'async methods returning Task<bool>', 'classes implementing IDisposable', 'methods with more than 5 parameters', 'static methods', 'virtual properties', 'generic classes', etc.")]
    public static Task<string> SemanticSearch(
        IWorkspaceExecutionGate gate,
        ICodePatternAnalyzer codePatternAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Semantic search query, e.g. 'async methods returning Task<bool>', 'classes implementing IDisposable', 'public static methods'")] string query,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var response = await codePatternAnalyzer.SemanticSearchAsync(workspaceId, query, project, limit, c);
                return JsonSerializer.Serialize(new
                {
                    count = response.Results.Count,
                    results = response.Results,
                    warning = response.Warning
                }, JsonDefaults.Indented);
            }, ct));
    }
}
