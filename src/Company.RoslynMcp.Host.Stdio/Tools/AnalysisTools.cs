using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AnalysisTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "project_diagnostics"), Description("Get compiler diagnostics (errors, warnings) for the workspace, optionally filtered by project, file, or severity")]
    public static async Task<string> GetProjectDiagnostics(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by file path")] string? file = null,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        CancellationToken ct = default)
    {
        var results = await diagnosticService.GetDiagnosticsAsync(workspaceId, project, file, severity, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "diagnostic_details"), Description("Get detailed information and curated fix options for a specific diagnostic occurrence")]
    public static async Task<string> GetDiagnosticDetails(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var result = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "type_hierarchy"), Description("Get the type hierarchy (base types, derived types, implemented interfaces) for a type at the given position")]
    public static async Task<string> GetTypeHierarchy(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var result = await symbolService.GetTypeHierarchyAsync(
            workspaceId,
            CreateLocator(filePath, line, column, symbolHandle),
            ct);
        if (result is null) return """{"error": "No type found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "callers_callees"), Description("Find direct callers and callees of a method at the given position")]
    public static async Task<string> GetCallersCallees(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var result = await symbolService.GetCallersCalleesAsync(
            workspaceId,
            CreateLocator(filePath, line, column, symbolHandle),
            ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "impact_analysis"), Description("Analyze the impact of changing a symbol: find all references, affected declarations, and affected projects")]
    public static async Task<string> AnalyzeImpact(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var result = await symbolService.AnalyzeImpactAsync(
            workspaceId,
            CreateLocator(filePath, line, column, symbolHandle),
            ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static SymbolLocator CreateLocator(string? filePath, int? line, int? column, string? symbolHandle)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
        {
            return SymbolLocator.ByHandle(symbolHandle);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            return SymbolLocator.BySource(filePath, line.Value, column.Value);
        }

        throw new ArgumentException("Provide either filePath/line/column or symbolHandle.");
    }
}
