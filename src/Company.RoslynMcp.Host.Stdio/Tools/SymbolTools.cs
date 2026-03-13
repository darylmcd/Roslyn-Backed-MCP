using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
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
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Search query pattern (supports partial matching)")] string query,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by symbol kind (Class, Method, Property, Field, Interface, etc.)")] string? kind = null,
        [Description("Optional: filter by namespace")] string? @namespace = null,
        [Description("Maximum number of results to return (default: 20)")] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await symbolService.SearchSymbolsAsync(workspaceId, query, project, kind, @namespace, limit, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "symbol_info"), Description("Get detailed information about a symbol at a specific file location")]
    public static async Task<string> GetSymbolInfo(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        var locator = CreateLocator(filePath, line, column, symbolHandle, metadataName);
        var result = await symbolService.GetSymbolInfoAsync(workspaceId, locator, ct);
        if (result is null) return """{"error": "No symbol found at the specified location"}""";
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "go_to_definition"), Description("Find the definition location(s) of a symbol at the given position")]
    public static async Task<string> GoToDefinition(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var locator = CreateLocator(filePath, line, column, symbolHandle, metadataName: null);
        var results = await symbolService.GoToDefinitionAsync(workspaceId, locator, ct);
        if (results.Count == 0) return """{"error": "No definition found for the symbol at the specified location"}""";
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "find_references"), Description("Find all references to a symbol at the given position across the entire solution")]
    public static async Task<string> FindReferences(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var locator = CreateLocator(filePath, line, column, symbolHandle, metadataName: null);
        var results = await symbolService.FindReferencesAsync(workspaceId, locator, ct);
        return JsonSerializer.Serialize(new { count = results.Count, references = results }, JsonOptions);
    }

    [McpServerTool(Name = "find_implementations"), Description("Find all implementations of an interface or abstract member at the given position")]
    public static async Task<string> FindImplementations(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var locator = CreateLocator(filePath, line, column, symbolHandle, metadataName: null);
        var results = await symbolService.FindImplementationsAsync(workspaceId, locator, ct);
        return JsonSerializer.Serialize(new { count = results.Count, implementations = results }, JsonOptions);
    }

    [McpServerTool(Name = "document_symbols"), Description("Get all symbol declarations (types, methods, properties, fields) in a document as a hierarchical tree")]
    public static async Task<string> GetDocumentSymbols(
        ISymbolService symbolService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var results = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    private static SymbolLocator CreateLocator(
        string? filePath,
        int? line,
        int? column,
        string? symbolHandle,
        string? metadataName)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
        {
            return SymbolLocator.ByHandle(symbolHandle);
        }

        if (!string.IsNullOrWhiteSpace(metadataName))
        {
            return SymbolLocator.ByMetadataName(metadataName);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            return SymbolLocator.BySource(filePath, line.Value, column.Value);
        }

        throw new ArgumentException(
            "Provide either filePath/line/column, symbolHandle, or metadataName.");
    }
}
