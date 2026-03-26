using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AnalysisTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "project_diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get compiler diagnostics (errors, warnings) for the workspace, optionally filtered by project, file, or severity")]
    public static Task<string> GetProjectDiagnostics(
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by file path")] string? file = null,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await diagnosticService.GetDiagnosticsAsync(workspaceId, project, file, severity, c);
                return JsonSerializer.Serialize(results, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "diagnostic_details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get detailed information and curated fix options for a specific diagnostic occurrence")]
    public static Task<string> GetDiagnosticDetails(
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get the type hierarchy (base types, derived types, implemented interfaces) for a type at the given position")]
    public static Task<string> GetTypeHierarchy(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
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
                var result = await symbolRelationshipService.GetTypeHierarchyAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle), c);
                if (result is null) throw new KeyNotFoundException("No type found at the specified location");
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "callers_callees", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find direct callers and callees of a method at the given position")]
    public static Task<string> GetCallersCallees(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
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
                var result = await symbolRelationshipService.GetCallersCalleesAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle), c);
                if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "impact_analysis", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Analyze the impact of changing a symbol: find all references, affected declarations, and affected projects")]
    public static Task<string> AnalyzeImpact(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
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
                var result = await mutationAnalysisService.AnalyzeImpactAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle), c);
                if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "find_type_mutations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Heavy analysis: find all mutating members of a type (settable properties, methods that write instance state) and their external callers, classified as construction-phase vs post-construction callers")]
    public static Task<string> FindTypeMutations(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the type")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle);
                var result = await mutationAnalysisService.FindTypeMutationsAsync(workspaceId, locator, c);
                if (result is null) throw new KeyNotFoundException("No named type found at the specified location");
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "find_type_usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all usages of a type across the solution, classified by role: MethodReturnType, MethodParameter, PropertyType, LocalVariable, FieldType, GenericArgument, BaseType, Cast, TypeCheck, ObjectCreation, or Other")]
    public static Task<string> FindTypeUsages(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the type")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. System.Collections.Generic.Dictionary`2")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
                var results = await mutationAnalysisService.FindTypeUsagesAsync(workspaceId, locator, c);
                var grouped = results
                    .GroupBy(u => u.Classification.ToString())
                    .ToDictionary(g => g.Key, g => g.ToList());
                return JsonSerializer.Serialize(new { count = results.Count, usagesByClassification = grouped }, JsonOptions);
            }, ct));
    }
}
