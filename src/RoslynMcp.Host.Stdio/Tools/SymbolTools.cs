using System.ComponentModel;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SymbolTools
{

    [McpServerTool(Name = "symbol_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Search for symbols (types, methods, properties, fields) by name pattern across the loaded workspace. Matching is substring (case-insensitive) — pass a bare fragment like 'Animal' to find 'AnimalService', 'IAnimal', 'CatAnimal', etc. Wildcards (*, ?) and regex metacharacters are NOT interpreted; they are matched literally. Pass `summary=true` to drop expensive per-symbol fields (documentation, parameters, baseTypes, interfaces, modifiers, returnType) for broad queries — useful when the default payload exceeds the MCP cap (~171 KB observed at limit=100 without summary). Response shape: { count, totalCount, hasMore, offset, limit, summary, symbols }.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Search symbols by name across the workspace.")]
    public static Task<string> SearchSymbols(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Search query — substring match (case-insensitive). Pass bare fragments, not wildcards or regex.")] string query,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: filter by symbol kind (Class, Method, Property, Field, Interface, etc.)")] string? kind = null,
        [Description("Optional: filter by namespace")] string? @namespace = null,
        [Description("Maximum number of results to return (default: 50, max: 50)")] int limit = 50,
        [Description("Number of results to skip before returning (default: 0)")] int offset = 0,
        [Description("When true, drops expensive per-symbol fields (documentation, parameters, baseTypes, interfaces, modifiers, returnType) to keep the response small for broad queries. Locator-essential fields (name, fullyQualifiedName, symbolHandle, kind, filePath, startLine, startColumn) remain populated. Default false preserves the full SymbolDto shape.")] bool summary = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(offset, limit);
            if (limit > 50)
                throw new ArgumentException($"Invalid limit '{limit}'. Limit must be 50 or less.");

            // Guard: empty/whitespace queries would otherwise dump the full workspace symbol index
            // (observed 70-80 KB payloads on mid-sized solutions). Return a structured empty-query
            // envelope so the caller gets an actionable note instead of a giant unfiltered dump.
            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonSerializer.Serialize(new
                {
                    count = 0,
                    totalCount = 0,
                    hasMore = false,
                    offset,
                    limit,
                    summary,
                    symbols = Array.Empty<object>(),
                    note = "query must be non-empty — pass a bare substring like 'Animal' to find 'AnimalService', 'IAnimal', etc."
                }, JsonDefaults.Indented);
            }

            // symbol-search-pagination: collect up to max(1000, offset+limit) results so that
            // totalCount accurately reflects the available pool within the internal cap.
            var maxResults = Math.Max(1000, offset + limit);
            var allResults = await symbolSearchService.SearchSymbolsAsync(workspaceId, query, projectName, kind, @namespace, maxResults, c);
            var paged = allResults.Skip(offset).Take(limit).ToList();
            var hasMore = offset + paged.Count < allResults.Count;

            // symbol-search-broad-query-response-cap-overflow: project paged results to a
            // summary shape (drops Documentation, Parameters, BaseTypes, Interfaces, Modifiers,
            // ReturnType) BEFORE serialization so the aggregate JSON stays under the MCP
            // inline transport cap. Mirrors the pattern in find_references but at the tool
            // wrapper (SymbolDto / ISymbolSearchService stay untouched).
            object ProjectSymbol(Core.Models.SymbolDto s) => summary
                ? (object)new
                {
                    name = s.Name,
                    fullyQualifiedName = s.FullyQualifiedName,
                    symbolHandle = s.SymbolHandle,
                    kind = s.Kind,
                    filePath = s.FilePath,
                    startLine = s.StartLine,
                    startColumn = s.StartColumn,
                }
                : s;

            // elicit-disambiguation-on-multi-symbol-resolve: when the search returns >1 candidate
            // and the client supports MCP elicitation, ask the agent which candidate to focus on
            // and return ONLY that one with a chosenViaElicitation marker. Purely additive: if the
            // client lacks elicitation OR the user declines, fall through to the existing list shape.
            if (paged.Count > 1 && StructuredCallToolFilter.HasElicitation(server?.ClientCapabilities))
            {
                var options = new List<(string Key, string Label)>(paged.Count);
                for (var i = 0; i < paged.Count; i++)
                {
                    var r = paged[i];
                    var label = string.IsNullOrEmpty(r.FilePath)
                        ? $"{r.Kind} {r.FullyQualifiedName}"
                        : $"{r.Kind} {r.FullyQualifiedName} — {Path.GetFileName(r.FilePath)}:{r.StartLine}";
                    options.Add((i.ToString(System.Globalization.CultureInfo.InvariantCulture), label));
                }

                var chosenKey = await StructuredCallToolFilter.TryElicitChoiceAsync(
                    server,
                    paramName: "choice",
                    title: "Pick a symbol",
                    description: $"symbol_search returned {allResults.Count} candidates for '{query}'. Pick one to focus on.",
                    options,
                    c).ConfigureAwait(false);

                if (chosenKey is not null
                    && int.TryParse(chosenKey, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var idx)
                    && idx >= 0 && idx < paged.Count)
                {
                    return JsonSerializer.Serialize(new
                    {
                        count = 1,
                        totalCount = allResults.Count,
                        hasMore,
                        offset,
                        limit,
                        summary,
                        symbols = new[] { ProjectSymbol(paged[idx]) },
                        chosenViaElicitation = true,
                    }, JsonDefaults.Indented);
                }
            }

            return JsonSerializer.Serialize(new
            {
                count = paged.Count,
                totalCount = allResults.Count,
                hasMore,
                offset,
                limit,
                summary,
                symbols = paged.Select(ProjectSymbol).ToList(),
            }, JsonDefaults.Indented);
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
            if (result is null)
            {
                // symbol-info-not-found-message-locator-vs-location: name the locator field the
                // caller actually supplied instead of the legacy "at the specified location" text,
                // which misled agents who passed only metadataName (no location at all).
                throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));
            }
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "go_to_definition", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find the definition location(s) of a symbol at the given position")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Navigate to the symbol definition.")]
    public static Task<string> GoToDefinition(
        McpServer server,
        IWorkspaceManager workspaceManager,
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

            // elicit-disambiguation-on-multi-symbol-resolve: see find_references for rationale.
            var disambiguation = await TryDisambiguateMetadataNameAsync(
                server, workspaceManager, workspaceId, locator, "go_to_definition", c).ConfigureAwait(false);
            if (disambiguation.ListEnvelope is not null)
            {
                return disambiguation.ListEnvelope;
            }
            if (disambiguation.ChosenLocator is not null)
            {
                locator = disambiguation.ChosenLocator;
            }

            var results = await symbolNavigationService.GoToDefinitionAsync(workspaceId, locator, c);
            if (results.Count == 0) throw new KeyNotFoundException("No definition found for the symbol at the specified location");
            return JsonSerializer.Serialize(new { count = results.Count, locations = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_references", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all references to a symbol at the given position across the entire solution. Response shape: { count, totalCount, hasMore, offset, limit, items } where items is the paged LocationDto list. Pass `summary=true` to drop per-ref preview text — useful for high-fan-out symbols where the default payload exceeds the MCP cap (Jellyfin's IUserManager: 154 KB on 233 refs). Optional `projectFilter` (case-sensitive Project.Name; comma-separated for multi) restricts the result to references hosted in the listed project(s) — matches semantic_grep's filter semantics.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find references to a symbol. Accepts an optional projectFilter (case-sensitive Project.Name; comma-separated).")]
    public static Task<string> FindReferences(
        McpServer server,
        IWorkspaceManager workspaceManager,
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
        [Description("Optional: case-sensitive Project.Name filter to scope the result; comma-separated for multiple projects (e.g. 'Foo.Core,Foo.Tests'). Null/empty preserves the unfiltered solution-wide walk.")] string? projectFilter = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(offset, limit);
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);

            // elicit-disambiguation-on-multi-symbol-resolve: when locator.HasMetadataName and the
            // name resolves to multiple candidates (overloads, partial classes, member-vs-type
            // collisions), try elicitation first; if accepted, swap the locator for the chosen
            // symbol's stable handle and continue. If unsupported / declined, return the
            // disambiguation list response (additive). Position-based locators already pin a
            // single symbol, so this branch is metadata-name-only.
            var disambiguation = await TryDisambiguateMetadataNameAsync(
                server, workspaceManager, workspaceId, locator, "find_references", c).ConfigureAwait(false);
            if (disambiguation.ListEnvelope is not null)
            {
                return disambiguation.ListEnvelope;
            }
            if (disambiguation.ChosenLocator is not null)
            {
                locator = disambiguation.ChosenLocator;
            }

            var filterSet = ParseProjectFilter(projectFilter);
            var results = await referenceService.FindReferencesAsync(workspaceId, locator, c, summary, filterSet);
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

    [McpServerTool(Name = "document_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get all symbol declarations (types, methods, properties, fields) in a document as a hierarchical tree. Accepts either filePath OR a symbol locator (symbolHandle / metadataName) — mirroring the flexibility of symbol_info. Response shape: { count, symbols, deprecation } — deprecation is null on the canonical tool and populated on aliases (e.g. get_symbol_outline).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "List declared symbols in a document.")]
    public static Task<string> GetDocumentSymbols(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return GetDocumentSymbolsCore(server, gate, symbolSearchService, workspaceId, filePath, symbolHandle, metadataName, deprecation: null, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: shared core invoked by both the canonical
    // `document_symbols` tool and the `get_symbol_outline` alias. The alias passes a populated
    // `deprecation` envelope so callers can see the canonical name inline; the canonical method
    // passes `null` so the response schema always carries the field.
    // document-symbols-accepts-symbol-handle: when symbolHandle or metadataName is provided,
    // delegates to the locator-based service overload which resolves the handle to a source path.
    // When only filePath is provided, delegates to the original filePath-based overload.
    internal static Task<string> GetDocumentSymbolsCore(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISymbolSearchService symbolSearchService,
        string workspaceId,
        string? filePath,
        string? symbolHandle,
        string? metadataName,
        ToolAliasDeprecation? deprecation,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            IReadOnlyList<Core.Models.DocumentSymbolDto> results;

            if (!string.IsNullOrWhiteSpace(symbolHandle) || !string.IsNullOrWhiteSpace(metadataName))
            {
                // Locator-driven path: resolve handle/metadataName to a source file, then collect.
                var locator = SymbolLocatorFactory.Create(filePath, null, null, symbolHandle, metadataName);
                results = await symbolSearchService.GetDocumentSymbolsAsync(workspaceId, locator, c).ConfigureAwait(false);
            }
            else
            {
                // Classic filePath path — validate against workspace roots before the service call.
                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("Provide either a filePath, a symbolHandle, or a metadataName.");
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                results = await symbolSearchService.GetDocumentSymbolsAsync(workspaceId, filePath, c).ConfigureAwait(false);
            }

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
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        CancellationToken ct = default)
    {
        return GetDocumentSymbolsCore(
            server,
            gate,
            symbolSearchService,
            workspaceId,
            filePath,
            symbolHandle,
            metadataName,
            ToolAliasDeprecation.ForSisterAlias("document_symbols"),
            ct);
    }

    [McpServerTool(Name = "find_overrides", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find true virtual/abstract overrides for a member — only symbols actually marked `override` of a virtual or abstract declaration. Sibling interface implementations (e.g. independent IDisposable.Dispose impls across a solution) are NOT included here; query member_hierarchy and read the siblingInterfaceImplementations bucket for that. Response shape: { count, items } in the normal path, or { count: 0, items: [], hint } when the post-promotion symbol is a corlib virtual (System.Object.ToString / Equals / GetHashCode, System.IDisposable.Dispose, etc.) — the unbounded solution-wide enumeration is suppressed and the hint explains why. Each item is a SymbolDto (Name, FullyQualifiedName, FilePath, StartLine, etc.). Auto-promotes to the virtual/interface root: override chains, explicit interface implementations, and implicit interface implementations are normalized before the search so callers can anchor at the implementation or declaration site and get the same result set. Metadata-boundary members (e.g. IEquatable<T>.Equals) surface with FilePath=null so the count matches member_hierarchy.overrides.")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Find overrides of a virtual or abstract member.")]
    public static Task<string> FindOverrides(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspaceManager,
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

            // find-overrides-payload-overflow-on-corlib-virtual (gh #754): resolve and promote
            // the symbol up-front so that callers anchored on a corlib virtual (or on an
            // override site whose root is a corlib virtual) get an explanatory hint instead of
            // a silently-empty response. The service-side guard in ReferenceService.FindOverridesAsync
            // also returns empty for the corlib case (defense-in-depth for member_hierarchy.overrides),
            // but the service surface returns IReadOnlyList<SymbolDto> with no hint channel, so the
            // wrapper layer is where the explanatory text lives.
            var solution = workspaceManager.GetCurrentSolution(workspaceId);
            var resolved = await SymbolResolver.ResolveAsync(solution, locator, c).ConfigureAwait(false);
            if (resolved is not null && ReferenceService.IsCorlibVirtualMember(ReferenceService.PromoteToVirtualRoot(resolved)))
            {
                var hint =
                    "Resolved to a corlib virtual member (e.g. System.Object.ToString / Equals / " +
                    "GetHashCode, System.IDisposable.Dispose) — overrides list suppressed to avoid " +
                    "an unbounded solution-wide enumeration. To enumerate concrete overrides, use " +
                    "symbol_search to locate the specific subclass first and then call find_overrides " +
                    "on a non-corlib virtual in your own hierarchy, or call semantic_grep for the " +
                    "method signature you need.";
                return JsonSerializer.Serialize(
                    new { count = 0, items = Array.Empty<SymbolDto>(), hint },
                    JsonDefaults.Indented);
            }

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

    [McpServerTool(Name = "member_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get a summary of base members, true virtual/abstract overrides, and sibling interface implementations for a symbol. Response buckets: `baseMembers` (the base chain), `overrides` (members actually marked `override` of a virtual/abstract declaration — matches find_overrides), `siblingInterfaceImplementations` (every concrete type whose member fulfills the same interface contract — e.g. independent IDisposable.Dispose impls across a solution).")]
    [McpToolMetadata("symbols", "stable", true, false,
        "Summarize base, override, and sibling interface implementation relationships for a member.")]
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
            if (result is null) throw new KeyNotFoundException(SymbolLocatorFactory.FormatSymbolNotFoundMessage(locator));
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
                hint = result.Hint,
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

    [McpServerTool(Name = "find_type_consumers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("File-granularity rollup of consumers for a named type. Returns one entry per source file that references the type, with a deduped list of usage `kinds` (using | ctor | inherit | field | local | other) and the total `count` of reference sites in that file. Removes the Grep fallback for 'which files touch this type' workflows. Pass typeName as a fully qualified metadata name (e.g. 'SampleLib.IAnimal' or 'System.Collections.Generic.List`1') or a short type name when unambiguous; generic arity uses backtick notation. Response shape: { count, items: [{ filePath, kinds, count }] } sorted by descending site count then ascending file path.")]
    [McpToolMetadata("symbols", "experimental", true, false,
        "Roll up reference sites per file for a named type, classified by usage kind.")]
    public static Task<string> FindTypeConsumers(
        IWorkspaceExecutionGate gate,
        ITypeConsumersService typeConsumersService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Type name to resolve. Accepts a fully qualified metadata name (e.g. 'Namespace.TypeName' or 'List`1') or a short type name when unambiguous.")] string typeName,
        [Description("Maximum number of file rollup entries to return (default: 100)")] int limit = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await typeConsumersService.FindTypeConsumersAsync(workspaceId, typeName, limit, c);
            return JsonSerializer.Serialize(new { count = results.Count, items = results }, JsonDefaults.Indented);
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

    /// <summary>
    /// find-references-project-filter: parses the comma-separated <c>projectFilter</c> wire
    /// parameter into the <see cref="IReadOnlyCollection{T}"/> shape consumed by
    /// <see cref="IReferenceService.FindReferencesAsync"/> and
    /// <see cref="IConsumerAnalysisService.FindConsumersAsync"/>. Whitespace around each
    /// entry is trimmed and empty fragments are dropped; null / whitespace-only input maps to
    /// <c>null</c> so the underlying services skip the filter pass entirely.
    /// </summary>
    internal static IReadOnlyCollection<string>? ParseProjectFilter(string? projectFilter)
    {
        if (string.IsNullOrWhiteSpace(projectFilter)) return null;
        var entries = projectFilter
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Length == 0 ? null : entries;
    }

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: outcome of the disambiguation gate.
    /// At most one of <see cref="ChosenLocator"/> / <see cref="ListEnvelope"/> is non-null;
    /// both null means "no disambiguation needed — caller proceeds with the original locator".
    /// </summary>
    /// <param name="ChosenLocator">A new <see cref="SymbolLocator"/> bound to the chosen candidate's stable handle, when the user accepted an elicitation prompt.</param>
    /// <param name="ListEnvelope">A pre-serialized JSON envelope listing the candidates, when elicitation is unavailable / declined and the caller should short-circuit.</param>
    internal readonly record struct DisambiguationOutcome(
        SymbolLocator? ChosenLocator,
        string? ListEnvelope);

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: when <paramref name="locator"/> is a
    /// metadata-name locator that resolves to multiple symbols (overloads, partial-class
    /// siblings, member-vs-type collisions), try MCP elicitation. Returns:
    /// <list type="bullet">
    ///   <item><b>ChosenLocator</b> set: the user picked one — caller swaps the locator and continues.</item>
    ///   <item><b>ListEnvelope</b> set: client lacks elicitation OR user declined — caller returns the additive disambiguation-list JSON.</item>
    ///   <item>both null: no ambiguity (zero or one candidate) — caller proceeds with the original locator.</item>
    /// </list>
    /// Position-based and handle-based locators are not disambiguated here — they already pin one symbol.
    /// </summary>
    internal static async Task<DisambiguationOutcome> TryDisambiguateMetadataNameAsync(
        McpServer? server,
        IWorkspaceManager workspaceManager,
        string workspaceId,
        SymbolLocator locator,
        string toolName,
        CancellationToken ct)
    {
        if (!locator.HasMetadataName || string.IsNullOrEmpty(locator.MetadataName))
        {
            return default;
        }

        var solution = workspaceManager.GetCurrentSolution(workspaceId);
        var candidates = await SymbolHandleSerializer
            .FindAllByMetadataNameAsync(solution, locator.MetadataName, ct)
            .ConfigureAwait(false);

        if (candidates.Count <= 1)
        {
            // 0 → caller will surface NotFound from the underlying service.
            // 1 → unambiguous, no elicitation needed.
            return default;
        }

        // Build candidate display payloads ONCE — reused for the elicit prompt and the
        // fallback list response so the labels the user sees are byte-identical to the
        // labels a non-elicit client would receive.
        var labels = new List<string>(candidates.Count);
        var handles = new List<string>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            labels.Add(SymbolHandleSerializer.BuildDisplayLabel(candidates[i]));
            handles.Add(SymbolHandleSerializer.CreateHandle(candidates[i]));
        }

        // Try elicitation first.
        if (StructuredCallToolFilter.HasElicitation(server?.ClientCapabilities))
        {
            var options = new List<(string Key, string Label)>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                options.Add((i.ToString(System.Globalization.CultureInfo.InvariantCulture), labels[i]));
            }

            var chosenKey = await StructuredCallToolFilter.TryElicitChoiceAsync(
                server,
                paramName: "choice",
                title: "Pick a symbol",
                description:
                    $"{toolName}: '{locator.MetadataName}' resolves to {candidates.Count} candidates " +
                    "(overloads, partial classes, or inherited members). Pick which one to use.",
                options,
                ct).ConfigureAwait(false);

            if (chosenKey is not null
                && int.TryParse(chosenKey, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var idx)
                && idx >= 0 && idx < candidates.Count)
            {
                var chosenLocator = SymbolLocatorFactory.Create(
                    filePath: null, line: null, column: null,
                    symbolHandle: handles[idx], metadataName: null);
                return new DisambiguationOutcome(chosenLocator, null);
            }
            // user declined / cancelled / SDK error → fall through to the additive list shape.
        }

        // Fallback list envelope (byte-identical regardless of whether elicitation was tried).
        var entries = new List<object>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            entries.Add(new
            {
                label = labels[i],
                symbolHandle = handles[i],
                kind = candidates[i].Kind.ToString(),
            });
        }

        var envelope = JsonSerializer.Serialize(new
        {
            ambiguous = true,
            metadataName = locator.MetadataName,
            count = candidates.Count,
            candidates = entries,
            note = "Metadata name resolved to multiple symbols. Re-call this tool with the chosen 'symbolHandle'.",
        }, JsonDefaults.Indented);

        return new DisambiguationOutcome(null, envelope);
    }

}
