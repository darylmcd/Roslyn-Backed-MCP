using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AnalysisTools
{
    private enum DiagnosticBucket
    {
        Workspace,
        Compiler,
        Analyzer,
    }

    [McpServerTool(Name = "project_diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Get diagnostics for the workspace (compiler CS*, analyzers CA*/IDE*, and workspace load issues). " +
        "Contrast with compile_check (CS-only). Totals totalErrors/totalWarnings/totalInfo count the full queried scope and ignore the severity filter; " +
        "the severity parameter only narrows which rows are collected (default minimum severity is Info when omitted — Hidden is still excluded). " +
        "Use offset/limit to page; limit defaults to 200 to cap payload size. When hasMore is true, increase offset or narrow project/file filters. " +
        "Large solutions can take tens of seconds — prefer projectName or file filters.")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Return compiler diagnostics for a workspace.")]
    public static Task<string> GetProjectDiagnostics(
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: filter by file path")] string? file = null,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden). Omit for Info floor.")] string? severity = null,
        [Description("Optional: filter to a specific diagnostic ID (e.g., CS8019, CA1000)")] string? diagnosticId = null,
        [Description("Number of diagnostics to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum diagnostics to return per call (default: 200); primary payload cap.")] int limit = 200,
        [Description("When true, return only per-project summary counts (no individual diagnostics). 10-100x smaller payload.")] bool summary = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateSeverity(severity);
        ParameterValidation.ValidatePagination(offset, limit);
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ProgressHelper.Report(progress, 0);
            var results = await diagnosticService.GetDiagnosticsAsync(workspaceId, projectName, file, severity, diagnosticId, c);
            ProgressHelper.Report(progress, 1, 1);

                var allDiagnostics = results.WorkspaceDiagnostics
                    .Select(diagnostic => (Bucket: DiagnosticBucket.Workspace, Diagnostic: diagnostic))
                    .Concat(results.CompilerDiagnostics.Select(diagnostic => (Bucket: DiagnosticBucket.Compiler, Diagnostic: diagnostic)))
                    .Concat(results.AnalyzerDiagnostics.Select(diagnostic => (Bucket: DiagnosticBucket.Analyzer, Diagnostic: diagnostic)))
                    .ToList();

                var pagedDiagnostics = allDiagnostics
                    .Skip(offset)
                    .Take(limit)
                    .ToList();

                var hasMore = offset + pagedDiagnostics.Count < allDiagnostics.Count;

                var restoreHint = allDiagnostics.Any(entry =>
                    entry.Bucket == DiagnosticBucket.Compiler &&
                    (entry.Diagnostic.Id == "CS0234" ||
                     (entry.Diagnostic.Message?.Contains("could not be found", StringComparison.OrdinalIgnoreCase) == true) ||
                     (entry.Diagnostic.Message?.Contains("does not exist in the namespace", StringComparison.OrdinalIgnoreCase) == true)));

                var restoreHintText = restoreHint
                    ? "Many missing-type errors often mean NuGet restore has not been run. Run `dotnet restore` on the solution, then `workspace_reload`."
                    : (string?)null;

                // Summary mode: counts by diagnostic ID, no individual diagnostic rows.
                // 10-100x smaller payload for large solutions.
                if (summary)
                {
                    var diagnosticGroups = allDiagnostics
                        .GroupBy(entry => entry.Diagnostic.Id)
                        .Select(group => new
                        {
                            id = group.Key,
                            count = group.Count(),
                            severity = group.First().Diagnostic.Severity,
                            category = group.First().Diagnostic.Category,
                        })
                        .OrderByDescending(g => g.severity == "Error" ? 0 : g.severity == "Warning" ? 1 : 2)
                        .ThenByDescending(g => g.count)
                        .ToList();

                    return JsonSerializer.Serialize(new
                    {
                        summary = true,
                        totalErrors = results.TotalErrors,
                        totalWarnings = results.TotalWarnings,
                        totalInfo = results.TotalInfo,
                        totalDiagnostics = allDiagnostics.Count,
                        distinctDiagnosticIds = diagnosticGroups.Count,
                        restoreHint = restoreHintText,
                        diagnosticGroups,
                    }, JsonDefaults.Indented);
                }

                return JsonSerializer.Serialize(new
                {
                    totalErrors = results.TotalErrors,
                    totalWarnings = results.TotalWarnings,
                    totalInfo = results.TotalInfo,
                    compilerErrors = results.CompilerErrors,
                    analyzerErrors = results.AnalyzerErrors,
                    workspaceErrors = results.WorkspaceErrors,
                    totalDiagnostics = allDiagnostics.Count,
                    offset,
                    limit,
                    returnedDiagnostics = pagedDiagnostics.Count,
                    hasMore,
                    paginationNote = hasMore
                        ? "More diagnostics exist in this scope; increase offset, raise limit, or narrow project/file/diagnosticId filters."
                        : null,
                    restoreHint = restoreHintText,
                    workspaceDiagnostics = pagedDiagnostics
                        .Where(entry => entry.Bucket == DiagnosticBucket.Workspace)
                        .Select(entry => entry.Diagnostic)
                        .ToList(),
                    compilerDiagnostics = pagedDiagnostics
                        .Where(entry => entry.Bucket == DiagnosticBucket.Compiler)
                        .Select(entry => entry.Diagnostic)
                        .ToList(),
                analyzerDiagnostics = pagedDiagnostics
                    .Where(entry => entry.Bucket == DiagnosticBucket.Analyzer)
                    .Select(entry => entry.Diagnostic)
                    .ToList(),
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "diagnostic_details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Get detailed information and available code fix options for a specific diagnostic occurrence. " +
        "supportedFixes is populated from CodeFixProvider instances loaded via the CodeFixProviderRegistry " +
        "(static IDE Features providers + per-project analyzer references). It will be empty when no provider is loaded for " +
        "the diagnostic id; in that case guidanceMessage points to get_code_actions + preview_code_action as the fallback. " +
        "Position parameters accept either `line`/`column` or the `startLine`/`startColumn` naming used by other positional tools " +
        "(find_references, goto_definition, get_code_actions, …); supply exactly one pair.")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Inspect one diagnostic occurrence in detail.")]
    public static Task<string> GetDiagnosticDetails(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number (alias: startLine). Supply exactly one of line/startLine.")] int? line = null,
        [Description("1-based column number (alias: startColumn). Supply exactly one of column/startColumn.")] int? column = null,
        [Description("Alias for line, matching the positional-tool convention used by find_references, goto_definition, etc.")] int? startLine = null,
        [Description("Alias for column, matching the positional-tool convention used by find_references, goto_definition, etc.")] int? startColumn = null,
        CancellationToken ct = default)
    {
        // Normalize line/column vs startLine/startColumn aliases. Reject ambiguous
        // (both supplied with different values) and missing-both cases so callers see
        // a clear parameter error instead of a confusing "diagnostic not found" envelope.
        var resolvedLine = ResolvePositionAlias(line, startLine, "line", "startLine");
        var resolvedColumn = ResolvePositionAlias(column, startColumn, "column", "startColumn");

        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, resolvedLine, resolvedColumn, c);
            if (result is null)
            {
                // FLAG-1C: surface a structured "not found" envelope instead of raw JSON null,
                // so downstream agents get an actionable error message they can route on.
                var notFound = new
                {
                    found = false,
                    diagnosticId,
                    filePath,
                    line = resolvedLine,
                    column = resolvedColumn,
                    message = $"No diagnostic with id '{diagnosticId}' was found at {filePath}:{resolvedLine}:{resolvedColumn}. " +
                              "Run project_diagnostics first and copy an exact (id, line, column) tuple from a real entry; " +
                              "diagnostic positions must match the analyzer-reported location, not just the surrounding line.",
                };
                return JsonSerializer.Serialize(notFound, JsonDefaults.Indented);
            }
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    /// <summary>
    /// Resolves a positional parameter that accepts two alias names (e.g. <c>line</c> and <c>startLine</c>).
    /// Exactly one of the two must be supplied; supplying both with conflicting values or supplying
    /// neither is a parameter error.
    /// </summary>
    private static int ResolvePositionAlias(int? primary, int? alias, string primaryName, string aliasName)
    {
        if (primary.HasValue && alias.HasValue)
        {
            if (primary.Value != alias.Value)
            {
                throw new ArgumentException(
                    $"Conflicting values for '{primaryName}' ({primary.Value}) and its alias '{aliasName}' ({alias.Value}). Supply only one.",
                    primaryName);
            }
            return primary.Value;
        }
        if (primary.HasValue) return primary.Value;
        if (alias.HasValue) return alias.Value;
        throw new ArgumentException(
            $"Missing required parameter '{primaryName}' (or its alias '{aliasName}').",
            primaryName);
    }

    [McpServerTool(Name = "type_hierarchy", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get the type hierarchy (base types, derived types, implemented interfaces) for a type at the given position")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Inspect type inheritance and interface relationships.")]
    public static Task<string> GetTypeHierarchy(
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
            var result = await symbolRelationshipService.GetTypeHierarchyAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName), c);
            if (result is null) throw new KeyNotFoundException("No type found at the specified location");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "callers_callees", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find direct callers and callees of the symbol resolved at the exact line/column (or symbolHandle). Resolution uses the token at that position — e.g. a caret on a field name inside a method resolves the field, not the enclosing method. Place the caret on the method name to analyze the method.")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Find direct callers and callees for a method.")]
    public static Task<string> GetCallersCallees(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Maximum number of callers to return (default: 100)")] int callersLimit = 100,
        [Description("Maximum number of callees to return (default: 100)")] int calleesLimit = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(0, callersLimit);
            ParameterValidation.ValidatePagination(0, calleesLimit);
            var result = await symbolRelationshipService.GetCallersCalleesAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName), c);
            if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");

            var callers = result.Callers.Take(callersLimit).ToList();
            var callees = result.Callees.Take(calleesLimit).ToList();
            var hasMoreCallers = result.Callers.Count > callers.Count;
            var hasMoreCallees = result.Callees.Count > callees.Count;

            return JsonSerializer.Serialize(new
            {
                symbol = result.Symbol,
                callers,
                callees,
                callersLimit,
                calleesLimit,
                hasMoreCallers,
                hasMoreCallees,
                totalCallers = result.Callers.Count,
                totalCallees = result.Callees.Count
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "impact_analysis", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Analyze the impact of changing a symbol: find all references, affected declarations, and affected projects. References and declarations are paginated server-side (FLAG-3D) — use referencesOffset/referencesLimit/declarationsLimit. Total counts and hasMore flags are always returned. Pass summary=true to drop the per-reference and per-declaration arrays and keep only counts (10-100x smaller payload on broad-impact symbols).")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Estimate the impact of changing a symbol.")]
    public static Task<string> AnalyzeImpact(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Number of references to skip before returning results (default: 0)")] int referencesOffset = 0,
        [Description("Maximum number of references to return per page (default: 100). Larger pages can blow MCP output budgets on broad-impact symbols.")] int referencesLimit = 100,
        [Description("Maximum number of affected declarations to return (default: 100)")] int declarationsLimit = 100,
        [Description("When true, drops the per-reference and per-declaration arrays and returns only the targetSymbol, affectedProjects list, totals, and hasMore flags. Default false preserves the v1.18.x shape.")] bool summary = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(referencesOffset, referencesLimit);
            if (declarationsLimit < 1) throw new ArgumentException("declarationsLimit must be >= 1.", nameof(declarationsLimit));
            var paging = new ImpactAnalysisPaging(referencesOffset, referencesLimit, declarationsLimit);
            var result = await mutationAnalysisService.AnalyzeImpactAsync(
                workspaceId,
                SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName),
                paging,
                c);
            if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");

            // Summary mode drops the materialized per-ref / per-decl arrays. Paging knobs
            // are still echoed so callers paging follow-up requests get the same envelope
            // shape regardless of mode. The downstream service still walks the full
            // reference set (needed to compute the totals); summary mode is a payload-size
            // reduction, not a compute reduction. See find_references(summary=true) for the
            // analogous symbol-tools contract.
            if (summary)
            {
                return JsonSerializer.Serialize(new
                {
                    targetSymbol = result.TargetSymbol,
                    affectedProjects = result.AffectedProjects,
                    summary = result.Summary,
                    totalDirectReferences = result.TotalDirectReferences,
                    totalAffectedDeclarations = result.TotalAffectedDeclarations,
                    hasMoreReferences = result.HasMoreReferences,
                    hasMoreDeclarations = result.HasMoreDeclarations,
                    referencesOffset = result.ReferencesOffset,
                    referencesLimit = result.ReferencesLimit,
                    declarationsLimit,
                    summaryMode = true,
                }, JsonDefaults.Indented);
            }

            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_type_mutations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Heavy analysis: find all mutating members of a type (settable properties, methods that write instance state) and their external callers, classified as construction-phase vs post-construction callers")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Identify mutating members of a type and who calls them.")]
    public static Task<string> FindTypeMutations(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the type")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Maximum number of mutating members to return (default: 100)")] int limit = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(0, limit);
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await mutationAnalysisService.FindTypeMutationsAsync(workspaceId, locator, c);
            if (result is null) throw new KeyNotFoundException("No named type found at the specified location");

            var mutatingMembers = result.MutatingMembers.Take(limit).ToList();
            var hasMore = result.MutatingMembers.Count > mutatingMembers.Count;
            return JsonSerializer.Serialize(new
            {
                type = result.Type,
                mutatingMembers,
                summary = result.Summary,
                limit,
                hasMore,
                totalMutatingMembers = result.MutatingMembers.Count
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_type_usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find all usages of a type across the solution, classified by role: MethodReturnType, MethodParameter, PropertyType, LocalVariable, FieldType, GenericArgument, BaseType, Cast, TypeCheck, ObjectCreation, or Other")]
    [McpToolMetadata("analysis", "stable", true, false,
        "Classify usages of a type across the solution.")]
    public static Task<string> FindTypeUsages(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file containing the type")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. System.Collections.Generic.Dictionary`2")] string? metadataName = null,
        [Description("Maximum number of usages to return (default: 100)")] int limit = 100,
        [Description("Number of usages to skip before returning results (default: 0)")] int offset = 0,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ParameterValidation.ValidatePagination(offset, limit);
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var results = await mutationAnalysisService.FindTypeUsagesAsync(workspaceId, locator, c);
            var paged = results.Skip(offset).Take(limit).ToList();
            // BUG-N11: Use PascalCase enum names for dictionary keys (Json may camelCase member names elsewhere).
            var grouped = paged
                .GroupBy(u => u.Classification.ToString(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var hasMore = offset + paged.Count < results.Count;
            return JsonSerializer.Serialize(new
            {
                count = paged.Count,
                totalCount = results.Count,
                hasMore,
                offset,
                limit,
                usagesByClassification = grouped
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "semantic_grep", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Token-aware regex search over the loaded C# workspace. Walks every document's syntax tokens (and comment trivia) and applies the supplied .NET regex to the text of tokens whose syntactic kind matches `scope`. Lets callers exclude false-positive matches that plain text grep would return inside string literals or comments. Scopes: `identifiers` (identifier tokens only), `strings` (string-literal tokens — verbatim, interpolated text, raw, char, utf8), `comments` (single-line, multi-line, and doc-comment trivia), or `all` (the union). Optional `projectFilter` restricts the walk by Project.Name. Hard-capped at 500 hits per call to bound response size — narrow `pattern` or `projectFilter` if hits are truncated. Response shape: { count, items: [{ filePath, line, column, tokenKind, snippet }] } sorted by ascending file/line/column.")]
    [McpToolMetadata("analysis", "experimental", true, false,
        "Token-aware regex search over C# code (identifier / string / comment scopes).")]
    public static Task<string> SemanticGrep(
        IWorkspaceExecutionGate gate,
        ISemanticGrepService semanticGrepService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description(".NET regex pattern to match against token text.")] string pattern,
        [Description("Token-kind scope: 'identifiers' | 'strings' | 'comments' | 'all'.")] string scope = "identifiers",
        [Description("Optional: case-sensitive Project.Name filter to scope the walk.")] string? projectFilter = null,
        [Description("Maximum hits to return (default 500; hard cap to bound response size).")] int limit = 500,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await semanticGrepService.SearchAsync(workspaceId, pattern, scope, projectFilter, limit, c);
            return JsonSerializer.Serialize(new { count = results.Count, items = results }, JsonDefaults.Indented);
        }, ct);
    }
}
