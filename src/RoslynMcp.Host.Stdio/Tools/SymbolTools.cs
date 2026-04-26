using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SymbolTools
{

    [McpServerTool(Name = "symbol_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Search for symbols (types, methods, properties, fields) by name pattern across the loaded workspace. Matching is substring (case-insensitive) — pass a bare fragment like 'Animal' to find 'AnimalService', 'IAnimal', 'CatAnimal', etc. Wildcards (*, ?) and regex metacharacters are NOT interpreted; they are matched literally.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Search symbols by name across the workspace.")]
    public static Task<string> SearchSymbols(
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Search query — substring match (case-insensitive). Pass bare fragments, not wildcards or regex.")] string query,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: filter by symbol kind (Class, Method, Property, Field, Interface, etc.)")] string? kind = null,
        [Description("Optional: filter by namespace")] string? @namespace = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            // Guard: empty/whitespace queries would otherwise dump the full workspace symbol index
            // (observed 70-80 KB payloads on mid-sized solutions). Return a structured empty-query
            // envelope so the caller gets an actionable note instead of a giant unfiltered dump.
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonSerializer.Serialize(new
                {
                    count = 0,
                    symbols = Array.Empty<object>(),
                    note = "query must be non-empty — pass a bare substring like 'Animal' to find 'AnimalService', 'IAnimal', etc."
                }, JsonDefaults.Indented);
            }
            var results = await symbolSearchService.SearchSymbolsAsync(workspaceId, query, projectName, kind, @namespace, limit, c);
            return JsonSerializer.Serialize(new { count = results.Count, symbols = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "symbol_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get detailed information about a symbol at a specific file location. Default resolution is strict — a caret on whitespace adjacent to an identifier returns NotFound. Pass allowAdjacent=true to restore the pre-v1.19.1 lenient behavior where the resolver walks to the adjacent token.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Inspect the symbol at a source location.")]
    public static Task<string> GetSymbolInfo(
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Default false (strict). When true, the resolver walks to the preceding token when the exact-position lookup misses — restores the pre-v1.19.1 lenient behavior that could silently resolve whitespace to adjacent identifiers.")] bool allowAdjacent = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await symbolSearchService.GetSymbolInfoAsync(workspaceId, locator, c, allowAdjacent);
            if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "go_to_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find the definition location(s) of a symbol at the given position")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Navigate to the symbol definition.")]
    public static Task<string> GoToDefinition(
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
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
            var results = await symbolNavigationService.GoToDefinitionAsync(workspaceId, locator, c);
            if (results.Count == 0) throw new KeyNotFoundException("No definition found for the symbol at the specified location");
            return JsonSerializer.Serialize(new { count = results.Count, locations = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all references to a symbol at the given position across the entire solution. Response shape: { count, totalCount, hasMore, offset, limit, items } where items is the paged LocationDto list. Pass `summary=true` to drop per-ref preview text — useful for high-fan-out symbols where the default payload exceeds the MCP cap (Jellyfin's IUserManager: 154 KB on 233 refs).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find references to a symbol.")]
    public static Task<string> FindReferences(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Maximum number of references to return (default: 100)")] int limit = 100,
        [Description("Number of references to skip before returning results (default: 0)")] int offset = 0,
        [Description("When true, drops per-ref preview text to keep the response small for high-fan-out symbols. File path + line + column + classification still populated. Default false preserves the v1.18.2 shape.")] bool summary = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(offset, limit);
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var results = await referenceService.FindReferencesAsync(workspaceId, locator, c, summary);
            var paged = results.Skip(offset).Take(limit).ToList();
            var hasMore = offset + paged.Count < results.Count;
            return JsonSerializer.Serialize(new
            {
                count = paged.Count,
                totalCount = results.Count,
                hasMore,
                offset,
                limit,
                summary,
                items = paged
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_implementations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all implementations of an interface or abstract member at the given position. Response shape: { count, items }. By default, source-generator-emitted partial declarations (e.g. Logging.g.cs, RegexGenerator.g.cs) are deduped against the user-authored partial so each implementation appears exactly once; pass includeGeneratedPartials=true to restore the raw per-declaration list. IMPORTANT: when using filePath/line/column, the column must point at the symbol identifier token (e.g., the interface name 'IMyService'), not the start of the line — otherwise no symbol can be resolved and the result is empty. For interface lookups, prefer metadataName (fully qualified) when you do not have an exact cursor position.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find implementations of an interface or abstract member.")]
    public static Task<string> FindImplementations(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number — must point at the symbol name token")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.IMyInterface")] string? metadataName = null,
        [Description("When true, emit every partial-declaration location (including generator-emitted .g.cs files). Default false dedupes to user-authored partials.")] bool includeGeneratedPartials = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var results = await referenceService.FindImplementationsAsync(workspaceId, locator, c, includeGeneratedPartials);
            return JsonSerializer.Serialize(new { count = results.Count, items = results, includeGeneratedPartials }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "document_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get all symbol declarations (types, methods, properties, fields) in a document as a hierarchical tree. Response shape: { count, symbols, deprecation } — deprecation is null on the canonical tool and populated on aliases (e.g. get_symbol_outline).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "List declared symbols in a document.")]
    public static Task<string> GetDocumentSymbols(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return GetDocumentSymbolsCore(server, gate, symbolSearchService, workspaceId, filePath, deprecation: null, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: shared core invoked by both the canonical
    // `document_symbols` tool and the `get_symbol_outline` alias. The alias passes a populated
    // `deprecation` envelope so callers can see the canonical name inline; the canonical method
    // passes `null` so the response schema always carries the field.
    internal static Task<string> GetDocumentSymbolsCore(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        string workspaceId,
        string filePath,
        ToolAliasDeprecation? deprecation,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var results = await symbolSearchService.GetDocumentSymbolsAsync(workspaceId, filePath, c);
            return JsonSerializer.Serialize(new { count = results.Count, symbols = results, deprecation }, JsonDefaults.Indented);
        }, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: thin alias for callers carrying the python-refactor
    // (Jedi) tool name `get_symbol_outline`. Delegates to the canonical `document_symbols`
    // implementation and surfaces the migration path inline via the `deprecation` envelope.
    [McpServerTool(Name = "get_symbol_outline", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Alias for `document_symbols` (cross-MCP-server name compatibility — matches the python-refactor tool name). Returns the canonical document_symbols response envelope ({ count, symbols, deprecation }) with deprecation.canonicalName populated. Prefer `document_symbols` directly in new code.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Alias for document_symbols (cross-MCP-server name compatibility).")]
    public static Task<string> GetSymbolOutline(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return GetDocumentSymbolsCore(
            server,
            gate,
            symbolSearchService,
            workspaceId,
            filePath,
            ToolAliasDeprecation.ForSisterAlias("document_symbols"),
            ct);
    }

    [McpServerTool(Name = "find_overrides", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find overriding members for a virtual, abstract, or interface member. Response shape: { count, items }; each item is a SymbolDto (Name, FullyQualifiedName, FilePath, StartLine, etc.). Auto-promotes to the virtual/interface root: override chains, explicit interface implementations, and implicit interface implementations are normalized before the search so callers can anchor at the implementation or declaration site and get the same result set. Metadata-boundary members (e.g. IEquatable<T>.Equals) now surface with FilePath=null so count matches member_hierarchy.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find overrides of a virtual or abstract member.")]
    public static Task<string> FindOverrides(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
            var results = await referenceService.FindOverridesAsync(workspaceId, locator, c);
            return JsonSerializer.Serialize(new { count = results.Count, items = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_base_members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find base or implemented members for an override or implementation. Response shape: { count, items }; each item is a SymbolDto (Name, FullyQualifiedName, FilePath, StartLine, etc.). Metadata-boundary bases (e.g. IEquatable<T>.Equals from corlib) surface with FilePath=null so count matches member_hierarchy.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find base or implemented members.")]
    public static Task<string> FindBaseMembers(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
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
            var results = await referenceService.FindBaseMembersAsync(workspaceId, locator, c);
            return JsonSerializer.Serialize(new { count = results.Count, items = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "member_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get a summary of base members and overrides for a symbol")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Summarize base and override relationships for a member.")]
    public static Task<string> GetMemberHierarchy(
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
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await symbolRelationshipService.GetMemberHierarchyAsync(workspaceId, locator, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "symbol_signature_help", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get display signature, parameters, return type, and documentation for the symbol resolved at the exact line/column (or handle/metadata name). When the caret lands on a method's return-type token (or a property's type token), the result is auto-promoted to the enclosing member by default — disable with preferDeclaringMember=false to inspect the type token directly.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Return symbol signature and documentation.")]
    public static Task<string> GetSignatureHelp(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("When true (default), a caret on a method's return-type token or a property's type token is auto-promoted to the enclosing member symbol. Set to false to resolve the type token literally.")] bool preferDeclaringMember = true,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await symbolRelationshipService.GetSignatureHelpAsync(workspaceId, locator, preferDeclaringMember, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "symbol_relationships", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get a combined summary of definitions, references, implementations, base members, and overrides. Auto-promotes a caret on a member's type token to the enclosing member by default (see preferDeclaringMember).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Combine definition, reference, base, and implementation relationships.")]
    public static Task<string> GetSymbolRelationships(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Maximum number of items to return per relationship bucket (default: 100)")] int limit = 100,
        [Description("When true (default), a caret on a method's return-type token or a property's type token is auto-promoted to the enclosing member symbol. Set to false to resolve the type token literally.")] bool preferDeclaringMember = true,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(0, limit);
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await symbolRelationshipService.GetSymbolRelationshipsAsync(workspaceId, locator, preferDeclaringMember, c);
            if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
            var references = result.References.Take(limit).ToList();
            var implementations = result.Implementations.Take(limit).ToList();
            var baseMembers = result.BaseMembers.Take(limit).ToList();
            var overrides = result.Overrides.Take(limit).ToList();
            var hasMore = result.References.Count > references.Count ||
                          result.Implementations.Count > implementations.Count ||
                          result.BaseMembers.Count > baseMembers.Count ||
                          result.Overrides.Count > overrides.Count;

            return JsonSerializer.Serialize(new
            {
                symbol = result.Symbol,
                definitions = result.Definitions,
                references,
                implementations,
                baseMembers,
                overrides,
                limit,
                hasMore,
                totals = new
                {
                    references = result.References.Count,
                    implementations = result.Implementations.Count,
                    baseMembers = result.BaseMembers.Count,
                    overrides = result.Overrides.Count
                }
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_references_bulk", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Find references for multiple symbols in one call (max 50). Returns { count, results } where each result has key, referenceCount, references, truncated, and optional error. " +
        "Parameter name must be `symbols` (array of objects). Do NOT pass symbolHandles or a JSON string array. " +
        "Each element must set exactly one of: symbolHandle, metadataName, or filePath+line+column. " +
        "Pass `summary=true` to drop per-ref preview text and `maxItemsPerSymbol=N` to cap each symbol's reference list so the aggregate envelope stays under the MCP payload cap (overflowed at 120 KB without bounds).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Resolve references for multiple symbols in one request.")]
    public static Task<string> FindReferencesBulk(
        IWorkspaceExecutionGate gate,
        IReferenceService referenceService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of locator objects (max 50). Example shape: symbols: [ { metadataName: \"SampleLib.IAnimal\" }, { filePath: \"C:/src/x.cs\", line: 10, column: 5 } ]")] BulkSymbolLocator[] symbols,
        [Description("Include the definition location in each result (default: false)")] bool includeDefinition = false,
        [Description("When true, drops per-ref preview text to keep each symbol's references small for high-fan-out batches. File path + line + column + classification still populated. Default false preserves the v1.18.2 shape.")] bool summary = false,
        [Description("Maximum number of reference locations to keep per symbol (default: 100). Applied BEFORE the outer envelope is assembled so the cap actually bounds aggregate output. Each result's `truncated` flag indicates whether its list was trimmed; `referenceCount` still reflects the full pre-cap total so callers can page follow-up queries via find_references.")] int maxItemsPerSymbol = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            if (maxItemsPerSymbol < 1)
                throw new ArgumentException("maxItemsPerSymbol must be >= 1.", nameof(maxItemsPerSymbol));

            var results = await referenceService.FindReferencesBulkAsync(workspaceId, symbols, includeDefinition, c);

            // Apply the per-symbol cap + summary stripping BEFORE assembling the outer envelope.
            // Serializing the raw service result and then paging post-hoc would still materialize
            // the full preview payload in memory and in the JSON output — the whole point of
            // summary/maxItemsPerSymbol is to bound the aggregate size, so the bound has to
            // happen upstream of JsonSerializer.Serialize.
            var shaped = new List<object>(results.Count);
            foreach (var r in results)
            {
                var fullCount = r.References.Count;
                var cappedCount = Math.Min(fullCount, maxItemsPerSymbol);
                var truncated = fullCount > cappedCount;

                IReadOnlyList<LocationDto> shapedRefs;
                if (summary || truncated)
                {
                    var buffer = new List<LocationDto>(cappedCount);
                    for (var i = 0; i < cappedCount; i++)
                    {
                        var loc = r.References[i];
                        buffer.Add(summary ? loc with { PreviewText = null } : loc);
                    }
                    shapedRefs = buffer;
                }
                else
                {
                    shapedRefs = r.References;
                }

                shaped.Add(new
                {
                    key = r.Key,
                    resolvedSymbol = r.ResolvedSymbol,
                    referenceCount = r.ReferenceCount,
                    returnedCount = shapedRefs.Count,
                    truncated,
                    references = shapedRefs,
                    error = r.Error,
                });
            }

            return JsonSerializer.Serialize(new
            {
                count = shaped.Count,
                summary,
                maxItemsPerSymbol,
                results = shaped,
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_property_writes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all locations where a property is assigned to (written). Each write carries a WriteKind bucket: ObjectInitializer (safe for init), Assignment (post-construction), OutRef (passed by out/ref), or PrimaryConstructorBind (the property is a positional-record primary-ctor parameter and the site is a `new T(value)` construction that binds this positional slot — find-property-writes-positional-record-silent-zero).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find property write sites and classify object-initializer writes.")]
    public static Task<string> FindPropertyWrites(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the property")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var (results, resolvedKind) = await mutationAnalysisService.FindPropertyWritesWithMetadataAsync(workspaceId, locator, c);
            // FLAG-3C: when the position resolves to a field or other non-property symbol,
            // return a structured disambiguation hint instead of a silent empty array.
            string? hint = null;
            if (results.Count == 0 && resolvedKind is not null && resolvedKind != "Property")
            {
                hint = $"Position resolved to a {resolvedKind}, not a property. Use find_references for fields and other symbol kinds.";
            }
            else if (results.Count == 0 && resolvedKind is null)
            {
                hint = "No symbol resolved at the given position. Verify the column points at the symbol identifier.";
            }
            return JsonSerializer.Serialize(new { count = results.Count, resolvedSymbolKind = resolvedKind, hint, writes = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "probe_position", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("position-probe-for-test-fixture-authoring: return the raw lexical + containing-symbol state at a source position without applying the lenient adjacent-identifier fallback used by symbol resolvers. Intended for test-fixture authoring — a caret on whitespace returns tokenKind='Whitespace' + leadingTriviaBefore=true instead of silently resolving to the next identifier. Response shape: { filePath, line, column, tokenKind, syntaxKind, tokenText, containingSymbol, containingSymbolKind, leadingTriviaBefore }.")]
    [McpToolMetadata("symbols", "experimental", true, false,
        "Probe the raw lexical token and containing symbol at a source position.")]
    public static Task<string> ProbePosition(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await symbolNavigationService.ProbePositionAsync(workspaceId, filePath, line, column, c);
            if (result is null) throw new KeyNotFoundException($"File '{filePath}' is not part of the loaded workspace.");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "enclosing_symbol", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find the enclosing symbol (method, property, type) at a given file position — useful for understanding the context of a cursor position")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Return the enclosing symbol for a source position.")]
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
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await symbolNavigationService.GetEnclosingSymbolAsync(workspaceId, filePath, line, column, c);
            if (result is null) throw new KeyNotFoundException("No enclosing symbol found at the specified location");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "goto_type_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Navigate to the type definition of a symbol (e.g., for a variable, go to its type's declaration rather than the variable's declaration)")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Navigate from a symbol usage to its type definition.")]
    public static Task<string> GoToTypeDefinition(
        IWorkspaceExecutionGate gate,
        ISymbolNavigationService symbolNavigationService,
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
            var results = await symbolNavigationService.GoToTypeDefinitionAsync(workspaceId, locator, c);
            if (results.Count == 0) throw new KeyNotFoundException("No type definition found for the symbol at the specified location");
            return JsonSerializer.Serialize(new { count = results.Count, locations = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "get_completions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get IntelliSense/code completion suggestions at a given position in a source file. Use filterText for case-insensitive prefix narrowing and maxItems for paging (UX-007). The response IsIncomplete flag indicates that the filtered list is longer than maxItems — raise maxItems or refine filterText to see the rest. InlineDescription may be empty when Roslyn does not supply inline text for an item.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Return IntelliSense-style completion items at a position.")]
    public static Task<string> GetCompletions(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ICompletionService completionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        [Description("Optional: case-insensitive prefix filter applied to candidate FilterText/DisplayText (UX-007)")] string? filterText = null,
        [Description("Maximum number of completion items to return (default: 100, must be > 0)")] int maxItems = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await completionService.GetCompletionsAsync(workspaceId, filePath, line, column, filterText, maxItems, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

}
