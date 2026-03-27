using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SymbolTools
{

    [McpServerTool(Name = "symbol_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Search for symbols (types, methods, properties, fields) by name pattern across the loaded workspace")]
    public static Task<string> SearchSymbols(
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Search query pattern (supports partial matching)")] string query,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by symbol kind (Class, Method, Property, Field, Interface, etc.)")] string? kind = null,
        [Description("Optional: filter by namespace")] string? @namespace = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await symbolSearchService.SearchSymbolsAsync(workspaceId, query, project, kind, @namespace, limit, c);
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "symbol_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get detailed information about a symbol at a specific file location")]
    public static Task<string> GetSymbolInfo(
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
                var result = await symbolSearchService.GetSymbolInfoAsync(workspaceId, locator, c);
                if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "go_to_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find the definition location(s) of a symbol at the given position")]
    public static Task<string> GoToDefinition(
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await symbolNavigationService.GoToDefinitionAsync(workspaceId, locator, c);
                if (results.Count == 0) throw new KeyNotFoundException("No definition found for the symbol at the specified location");
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all references to a symbol at the given position across the entire solution")]
    public static Task<string> FindReferences(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await referenceService.FindReferencesAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(new { count = results.Count, references = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all implementations of an interface or abstract member at the given position")]
    public static Task<string> FindImplementations(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await referenceService.FindImplementationsAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(new { count = results.Count, implementations = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "document_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get all symbol declarations (types, methods, properties, fields) in a document as a hierarchical tree")]
    public static Task<string> GetDocumentSymbols(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var results = await symbolSearchService.GetDocumentSymbolsAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_overrides", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find overriding members for a virtual, abstract, or interface member")]
    public static Task<string> FindOverrides(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await referenceService.FindOverridesAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_base_members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find base or implemented members for an override or implementation")]
    public static Task<string> FindBaseMembers(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await referenceService.FindBaseMembersAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "member_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get a summary of base members and overrides for a symbol")]
    public static Task<string> GetMemberHierarchy(
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var result = await symbolRelationshipService.GetMemberHierarchyAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "symbol_signature_help", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get display signature, parameters, return type, and documentation for a symbol")]
    public static Task<string> GetSignatureHelp(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
                var result = await symbolRelationshipService.GetSignatureHelpAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "symbol_relationships", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get a combined summary of definitions, references, implementations, base members, and overrides")]
    public static Task<string> GetSymbolRelationships(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
                var result = await symbolRelationshipService.GetSymbolRelationshipsAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_references_bulk", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find references for multiple symbols in a single call (max 50). Returns a list of results keyed by symbol handle, metadata name, or file:line:column")]
    public static Task<string> FindReferencesBulk(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of symbol locators (max 50). Each object may have symbolHandle, metadataName, or filePath/line/column")] BulkSymbolLocator[] symbols,
        [Description("Include the definition location in each result (default: false)")] bool includeDefinition = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await referenceService.FindReferencesBulkAsync(workspaceId, symbols, includeDefinition, c);
                return JsonSerializer.Serialize(new { count = results.Count, results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "find_property_writes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all locations where a property is assigned to (written), classified as object-initializer writes (safe for init) or post-construction assignments")]
    public static Task<string> FindPropertyWrites(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the property")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await mutationAnalysisService.FindPropertyWritesAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(new { count = results.Count, writes = results }, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "enclosing_symbol", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find the enclosing symbol (method, property, type) at a given file position — useful for understanding the context of a cursor position")]
    public static Task<string> GetEnclosingSymbol(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await symbolNavigationService.GetEnclosingSymbolAsync(workspaceId, filePath, line, column, c);
                if (result is null) throw new KeyNotFoundException("No enclosing symbol found at the specified location");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "goto_type_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Navigate to the type definition of a symbol (e.g., for a variable, go to its type's declaration rather than the variable's declaration)")]
    public static Task<string> GoToTypeDefinition(
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
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
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null);
                var results = await symbolNavigationService.GoToTypeDefinitionAsync(workspaceId, locator, c);
                if (results.Count == 0) throw new KeyNotFoundException("No type definition found for the symbol at the specified location");
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_completions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get IntelliSense/code completion suggestions at a given position in a source file")]
    public static Task<string> GetCompletions(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ICompletionService completionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await completionService.GetCompletionsAsync(workspaceId, filePath, line, column, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

}
