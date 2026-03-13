using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SymbolTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "symbol_search"), Description("Search for symbols (types, methods, properties, fields) by name pattern across the loaded workspace")]
    public static async Task<string> SearchSymbols(
        ISymbolService symbolService,
        [Description("Search query pattern (supports partial matching)")] string query,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by symbol kind (Class, Method, Property, Field, Interface, etc.)")] string? kind = null,
        [Description("Optional: filter by namespace")] string? @namespace = null,
        [Description("Maximum number of results to return (default: 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await symbolService.SearchSymbolsAsync(query, project, kind, @namespace, limit, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "symbol_info"), Description("Get detailed information about a symbol at a specific file location")]
    public static async Task<string> GetSymbolInfo(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var result = await symbolService.GetSymbolInfoAsync(filePath, line, column, ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "go_to_definition"), Description("Find the definition location(s) of a symbol at the given position")]
    public static async Task<string> GoToDefinition(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var results = await symbolService.GoToDefinitionAsync(filePath, line, column, ct);
        if (results.Count == 0) return """{"error": "No definition found for the symbol at the specified location"}""";
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "find_references"), Description("Find all references to a symbol at the given position across the entire solution")]
    public static async Task<string> FindReferences(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var results = await symbolService.FindReferencesAsync(filePath, line, column, ct);
        return JsonSerializer.Serialize(new { count = results.Count, references = results }, JsonOptions);
    }

    [McpServerTool(Name = "find_implementations"), Description("Find all implementations of an interface or abstract member at the given position")]
    public static async Task<string> FindImplementations(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var results = await symbolService.FindImplementationsAsync(filePath, line, column, ct);
        return JsonSerializer.Serialize(new { count = results.Count, implementations = results }, JsonOptions);
    }

    [McpServerTool(Name = "document_symbols"), Description("Get all symbol declarations (types, methods, properties, fields) in a document as a hierarchical tree")]
    public static async Task<string> GetDocumentSymbols(
        ISymbolService symbolService,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var results = await symbolService.GetDocumentSymbolsAsync(filePath, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }
}
