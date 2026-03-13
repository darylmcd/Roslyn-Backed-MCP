using System.ComponentModel;
using System.Text.Json;
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
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by file path")] string? file = null,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        CancellationToken ct = default)
    {
        var results = await diagnosticService.GetDiagnosticsAsync(project, file, severity, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "type_hierarchy"), Description("Get the type hierarchy (base types, derived types, implemented interfaces) for a type at the given position")]
    public static async Task<string> GetTypeHierarchy(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var result = await symbolService.GetTypeHierarchyAsync(filePath, line, column, ct);
        if (result is null) return """{"error": "No type found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "callers_callees"), Description("Find direct callers and callees of a method at the given position")]
    public static async Task<string> GetCallersCallees(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var result = await symbolService.GetCallersCalleesAsync(filePath, line, column, ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "impact_analysis"), Description("Analyze the impact of changing a symbol: find all references, affected declarations, and affected projects")]
    public static async Task<string> AnalyzeImpact(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var result = await symbolService.AnalyzeImpactAsync(filePath, line, column, ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
