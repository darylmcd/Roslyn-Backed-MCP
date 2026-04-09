using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;

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

    [McpServerTool(Name = "project_diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get diagnostics (errors, warnings, infos, hints) for the workspace, optionally filtered by project, file, or severity. Includes BOTH compiler diagnostics (CS*) AND analyzer diagnostics (CA*, IDE*) — contrast with compile_check which is CS-only. Severity filter behavior: totalError/totalWarning/totalInfo in the response reflect counts across the WHOLE unfiltered result set; the severity parameter filters the returned page, so it is possible to see totalInfo > 0 with an empty page when severity=Error or when Info-severity diagnostics are paged past by offset. Full-solution scans on large graphs can take 30–40s — narrow by project or file when possible, and raise offset/limit to page. Response JSON fields: totalErrors, totalWarnings, totalInfo (severity-filter invariant totals), compilerErrors, analyzerErrors, workspaceErrors (source-specific error counts), severityHint (present when the page is empty but lower-severity diagnostics exist). Default severity is Warning — pass severity='Info' to include Info-level diagnostics.")]
    public static Task<string> GetProjectDiagnostics(
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by file path")] string? file = null,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        [Description("Number of diagnostics to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum number of diagnostics to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("project_diagnostics", () =>
        {
            ParameterValidation.ValidateSeverity(severity);
            ParameterValidation.ValidatePagination(offset, limit);
            return gate.RunReadAsync(workspaceId, async c =>
            {
                var results = await diagnosticService.GetDiagnosticsAsync(workspaceId, project, file, severity, c);

                var allDiagnostics = results.WorkspaceDiagnostics
                    .Select(diagnostic => (Bucket: DiagnosticBucket.Workspace, Diagnostic: diagnostic))
                    .Concat(results.CompilerDiagnostics.Select(diagnostic => (Bucket: DiagnosticBucket.Compiler, Diagnostic: diagnostic)))
                    .Concat(results.AnalyzerDiagnostics.Select(diagnostic => (Bucket: DiagnosticBucket.Analyzer, Diagnostic: diagnostic)))
                    .ToList();

                var pagedDiagnostics = allDiagnostics
                    .Skip(offset)
                    .Take(limit)
                    .ToList();

                var restoreHint = allDiagnostics.Any(entry =>
                    entry.Bucket == DiagnosticBucket.Compiler &&
                    (entry.Diagnostic.Id == "CS0234" ||
                     (entry.Diagnostic.Message?.Contains("could not be found", StringComparison.OrdinalIgnoreCase) == true) ||
                     (entry.Diagnostic.Message?.Contains("does not exist in the namespace", StringComparison.OrdinalIgnoreCase) == true)));

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
                    hasMore = offset + pagedDiagnostics.Count < allDiagnostics.Count,
                    restoreHint = restoreHint
                        ? "Many missing-type errors often mean NuGet restore has not been run. Run `dotnet restore` on the solution, then `workspace_reload`."
                        : null,
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
        });
    }

    [McpServerTool(Name = "diagnostic_details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get detailed information and curated fix options for a specific diagnostic occurrence")]
    public static Task<string> GetDiagnosticDetails(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("diagnostic_details", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, c);
                if (result is null)
                {
                    // FLAG-1C: surface a structured "not found" envelope instead of raw JSON null,
                    // so downstream agents get an actionable error message they can route on.
                    var notFound = new
                    {
                        found = false,
                        diagnosticId,
                        filePath,
                        line,
                        column,
                        message = $"No diagnostic with id '{diagnosticId}' was found at {filePath}:{line}:{column}. " +
                                  "Run project_diagnostics first and copy an exact (id, line, column) tuple from a real entry; " +
                                  "diagnostic positions must match the analyzer-reported location, not just the surrounding line.",
                    };
                    return JsonSerializer.Serialize(notFound, JsonDefaults.Indented);
                }
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
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
        return ToolErrorHandler.ExecuteAsync("type_hierarchy", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await symbolRelationshipService.GetTypeHierarchyAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false), c);
                if (result is null) throw new KeyNotFoundException("No type found at the specified location");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "callers_callees", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find direct callers and callees of the symbol resolved at the exact line/column (or symbolHandle). Resolution uses the token at that position — e.g. a caret on a field name inside a method resolves the field, not the enclosing method. Place the caret on the method name to analyze the method.")]
    public static Task<string> GetCallersCallees(
        IWorkspaceExecutionGate gate,
        ISymbolRelationshipService symbolRelationshipService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Maximum number of callers to return (default: 100)")] int callersLimit = 100,
        [Description("Maximum number of callees to return (default: 100)")] int calleesLimit = 100,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("callers_callees", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                ParameterValidation.ValidatePagination(0, callersLimit);
                ParameterValidation.ValidatePagination(0, calleesLimit);
                var result = await symbolRelationshipService.GetCallersCalleesAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false), c);
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
            }, ct));
    }

    [McpServerTool(Name = "impact_analysis", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Analyze the impact of changing a symbol: find all references, affected declarations, and affected projects. References and declarations are paginated server-side (FLAG-3D) — use referencesOffset/referencesLimit/declarationsLimit. Total counts and hasMore flags are always returned.")]
    public static Task<string> AnalyzeImpact(
        IWorkspaceExecutionGate gate,
        IMutationAnalysisService mutationAnalysisService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Number of references to skip before returning results (default: 0)")] int referencesOffset = 0,
        [Description("Maximum number of references to return per page (default: 100). Larger pages can blow MCP output budgets on broad-impact symbols.")] int referencesLimit = 100,
        [Description("Maximum number of affected declarations to return (default: 100)")] int declarationsLimit = 100,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("impact_analysis", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                ParameterValidation.ValidatePagination(referencesOffset, referencesLimit);
                if (declarationsLimit < 1) throw new ArgumentException("declarationsLimit must be >= 1.", nameof(declarationsLimit));
                var paging = new ImpactAnalysisPaging(referencesOffset, referencesLimit, declarationsLimit);
                var result = await mutationAnalysisService.AnalyzeImpactAsync(
                    workspaceId,
                    SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false),
                    paging,
                    c);
                if (result is null) throw new KeyNotFoundException("No symbol found at the specified location");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
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
        [Description("Maximum number of mutating members to return (default: 100)")] int limit = 100,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("find_type_mutations", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                ParameterValidation.ValidatePagination(0, limit);
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false);
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
        [Description("Maximum number of usages to return (default: 100)")] int limit = 100,
        [Description("Number of usages to skip before returning results (default: 0)")] int offset = 0,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("find_type_usages", () =>
            gate.RunReadAsync(workspaceId, async c =>
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
            }, ct));
    }
}
