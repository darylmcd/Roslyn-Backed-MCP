using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class AdvancedAnalysisTools
{

    [McpServerTool(Name = "find_unused_symbols", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Find likely unused symbols."),
     Description("Find symbols (types, methods, properties, fields) with zero references across the solution — helps identify dead code. Each hit includes Confidence: high (private/internal), medium (public API), low (enum members, record/serialization-shaped properties, interface members — often false positives). By default skips convention-invoked shapes (EF ModelSnapshots, xUnit/NUnit/MSTest fixtures, ASP.NET middleware, SignalR Hubs, FluentValidation validators, Razor PageModels) — set excludeConventionInvoked=false to include them.")]
    public static Task<string> FindUnusedSymbols(
        IWorkspaceExecutionGate gate,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Include public symbols in the search (default: false, since public APIs may be consumed externally)")] bool includePublic = false,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        [Description("When true, skip enum members (often referenced indirectly).")] bool excludeEnums = false,
        [Description("When true, skip properties declared on record types (often DTO/serialization shaped).")] bool excludeRecordProperties = false,
        [Description("When true, skip projects whose names look like test projects (*.Tests, *Tests).")] bool excludeTestProjects = false,
        [Description("When true, skip symbols in test fixture types (xUnit/NUnit/MSTest-shaped names and attributes).")] bool excludeTests = false,
        [Description("When true (default), skip symbols matching convention-invoked shapes — EF ModelSnapshot, xUnit/MSTest/NUnit fixtures, ASP.NET middleware (Invoke/InvokeAsync(HttpContext)), SignalR Hubs, FluentValidation AbstractValidator<T>, Razor PageModel subclasses. Detection is name-shape based, so a custom class literally named 'Hub'/'PageModel'/etc. may also be excluded.")] bool excludeConventionInvoked = true,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await unusedCodeAnalyzer.FindUnusedSymbolsAsync(
                workspaceId,
                new UnusedSymbolsAnalysisOptions
                {
                    ProjectFilter = projectName,
                    IncludePublic = includePublic,
                    Limit = limit,
                    ExcludeEnums = excludeEnums,
                    ExcludeRecordProperties = excludeRecordProperties,
                    ExcludeTestProjects = excludeTestProjects,
                    ExcludeTests = excludeTests,
                    ExcludeConventionInvoked = excludeConventionInvoked
                },
                c);
            return JsonSerializer.Serialize(new { count = results.Count, unusedSymbols = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "get_di_registrations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Inspect DI registration patterns in source."),
     Description("Scan the solution for dependency injection registrations (AddSingleton, AddScoped, AddTransient) and return the service-to-implementation mappings. " +
        "Paginated via offset/limit (default limit=100) to bound response size on large DI graphs — callers on solutions with > limit registrations " +
        "should iterate by increasing offset until hasMore=false. totalCount counts ALL registrations in the queried scope; the paged registrations slice " +
        "contains only the [offset, offset+limit) window. Pass showLifetimeOverrides=true to additionally emit per-service-type override chains (winning lifetime, " +
        "lifetime-mismatch flag, dead-registration count) — opt-in to keep the default payload shape stable; pagination applies identically to the registrations list " +
        "in this mode while overrideChains remains unpaged (use summary=true if the override-chain list also needs paging). " +
        "Pass summary=true for large graphs to return aggregate counts plus a bounded page of override-chain summaries instead of full registrations/overrideChains.")]
    public static Task<string> GetDiRegistrations(
        IWorkspaceExecutionGate gate,
        IDiRegistrationService diRegistrationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("When true, also emit overrideChains[] grouping registrations by service type with the winning lifetime, lifetime-mismatch flag (Singleton vs Scoped vs Transient), and dead-registration count. Default: false (legacy shape: count + registrations[] — now augmented with totalCount/hasMore/offset/limit for paging).")] bool showLifetimeOverrides = false,
        [Description("When true, return a compact aggregate shape for large DI graphs. The detailed/default response is paginated via offset/limit when false.")] bool summary = false,
        [Description("0-based offset into the paginated response. Applies to the registrations list in detailed mode and to the override-chain summary page when summary=true.")] int offset = 0,
        [Description("Maximum items returned per call. Applies to the registrations list in detailed mode (default 100) and to the override-chain summary page when summary=true (clamped to [1, 500]).")] int limit = 100,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidatePagination(offset, limit);
        return gate.RunReadAsync(workspaceId, async c =>
        {
            if (!showLifetimeOverrides)
            {
                var results = await diRegistrationService.GetDiRegistrationsAsync(workspaceId, projectName, c);
                if (summary)
                {
                    return JsonSerializer.Serialize(
                        BuildDiRegistrationSummary(results, [], offset, limit),
                        JsonDefaults.Indented);
                }

                // gh #771: bound the detailed response to a paged window. Mirrors find_type_usages /
                // find_reflection_usages envelope — collect-all + Skip/Take; totalCount = full count;
                // hasMore = (offset + count) < totalCount.
                var pagedResults = results.Skip(offset).Take(limit).ToList();
                var resultsHasMore = offset + pagedResults.Count < results.Count;
                return JsonSerializer.Serialize(new
                {
                    count = pagedResults.Count,
                    totalCount = results.Count,
                    offset,
                    limit,
                    hasMore = resultsHasMore,
                    registrations = pagedResults,
                }, JsonDefaults.Indented);
            }

            // di-lifetime-mismatch-detection: opt-in path returns the (paged) registrations
            // list plus the per-service-type override chains. Override-chain output remains
            // unpaged in this mode — callers needing chain-level paging use summary=true.
            var scan = await diRegistrationService.GetDiRegistrationsWithOverridesAsync(workspaceId, projectName, c);
            if (summary)
            {
                return JsonSerializer.Serialize(
                    BuildDiRegistrationSummary(scan.Registrations, scan.OverrideChains, offset, limit),
                    JsonDefaults.Indented);
            }

            // gh #771: same paging contract for the registrations list when override chains
            // are also emitted. overrideChainCount/overrideChains continue to reflect the
            // full chain set unchanged.
            var pagedRegistrations = scan.Registrations.Skip(offset).Take(limit).ToList();
            var registrationsHasMore = offset + pagedRegistrations.Count < scan.Registrations.Count;
            return JsonSerializer.Serialize(new
            {
                count = pagedRegistrations.Count,
                totalCount = scan.Registrations.Count,
                offset,
                limit,
                hasMore = registrationsHasMore,
                registrations = pagedRegistrations,
                overrideChainCount = scan.OverrideChains.Count,
                overrideChains = scan.OverrideChains,
            }, JsonDefaults.Indented);
        }, ct);
    }

    private static DiRegistrationSummaryResultDto BuildDiRegistrationSummary(
        IReadOnlyList<DiRegistrationDto> registrations,
        IReadOnlyList<DiRegistrationOverrideChainDto> overrideChains,
        int offset,
        int limit)
    {
        var clampedOffset = Math.Clamp(offset, 0, overrideChains.Count);
        var clampedLimit = Math.Clamp(limit, 1, 500);
        var page = overrideChains
            .Skip(clampedOffset)
            .Take(clampedLimit)
            .Select(chain => new DiRegistrationOverrideChainSummaryDto(
                chain.ServiceType,
                chain.Registrations.Count,
                chain.WinningLifetime,
                chain.WinningImplementationType,
                chain.LifetimesDiffer,
                chain.DeadRegistrationCount))
            .ToList();

        return new DiRegistrationSummaryResultDto(
            Count: registrations.Count,
            DistinctServiceTypeCount: registrations
                .Select(registration => registration.ServiceType)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            ByLifetime: registrations
                .GroupBy(registration => registration.Lifetime, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            OverrideChainCount: overrideChains.Count,
            LifetimeMismatchCount: overrideChains.Count(chain => chain.LifetimesDiffer),
            DeadRegistrationCount: overrideChains.Sum(chain => chain.DeadRegistrationCount),
            Offset: clampedOffset,
            Limit: clampedLimit,
            HasMore: clampedOffset + page.Count < overrideChains.Count,
            OverrideChains: page);
    }

    [McpServerTool(Name = "get_complexity_metrics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Compute cyclomatic complexity and related metrics."),
     Description("Calculate cyclomatic complexity, lines of code, nesting depth, parameter count, and an approximate maintainability index (0–100, higher is better) for methods in the workspace")]
    public static Task<string> GetComplexityMetrics(
        IWorkspaceExecutionGate gate,
        ICodeMetricsService codeMetricsService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by source file path")] string? filePath = null,
        [Description("Optional: list of source file paths to include (union with filePath). Pass as a native JSON array of absolute file paths, not a JSON-encoded string. Example: [\"/abs/path/a.cs\", \"/abs/path/b.cs\"]. Empty list means no filter. Useful for re-running complexity on a changed-file set after a refactor.")] IReadOnlyList<string>? filePaths = null,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: minimum cyclomatic complexity threshold (default: return all)")] int? minComplexity = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await codeMetricsService.GetComplexityMetricsAsync(workspaceId, filePath, filePaths, projectName, minComplexity, limit, c);
            return JsonSerializer.Serialize(new { count = results.Count, metrics = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_reflection_usages", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Find reflection-heavy call sites."),
     Description("Find all reflection API usage in the solution (typeof, Type.GetMethod, Activator.CreateInstance, Assembly.Load, etc.). " +
        "Paginated via offset/limit (default limit=200) to bound response size on reflection-heavy solutions — " +
        "callers on solutions with > limit hits should iterate by increasing offset until hasMore=false. " +
        "totalCount counts ALL reflection sites in the queried scope; the paged usagesByKind slice contains only " +
        "the [offset, offset+limit) window. Pass summary=true for the per-UsageKind counts only (no item arrays) — " +
        "a compact aggregate shape for large solutions where the default paginated response still exceeds the MCP cap.")]
    public static Task<string> FindReflectionUsages(
        IWorkspaceExecutionGate gate,
        ICodePatternAnalyzer codePatternAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Number of usages to skip before returning results (default: 0).")] int offset = 0,
        [Description("Maximum number of usages to return per call (default: 200); primary payload cap.")] int limit = 200,
        [Description("When true, return only per-UsageKind aggregate counts (no item arrays). 10-100x smaller payload on reflection-heavy solutions.")] bool summary = false,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidatePagination(offset, limit);
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await codePatternAnalyzer.FindReflectionUsagesAsync(workspaceId, projectName, c);

            // Summary mode: drop the item arrays and return only per-UsageKind counts plus the
            // total. usageKindCounts is computed on the FULL result set (not the paged slice)
            // so callers see the true distribution regardless of paging. 10-100x smaller
            // payload on reflection-heavy solutions — matches the project_diagnostics(summary=true)
            // contract.
            if (summary)
            {
                var usageKindCounts = results
                    .GroupBy(r => r.UsageKind, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

                return JsonSerializer.Serialize(new
                {
                    summary = true,
                    count = 0,
                    totalCount = results.Count,
                    offset,
                    limit,
                    hasMore = false,
                    usageKindCounts,
                }, JsonDefaults.Indented);
            }

            // Default paginated mode mirrors FindTypeUsages: collect the full result set
            // (the walk cost is the same either way), slice with Skip/Take, then group the
            // paged slice by UsageKind for the wire envelope. count = paged slice size;
            // totalCount = full result set size; hasMore = (offset + count) < totalCount.
            var paged = results.Skip(offset).Take(limit).ToList();
            var grouped = paged
                .GroupBy(r => r.UsageKind, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var hasMore = offset + paged.Count < results.Count;

            return JsonSerializer.Serialize(new
            {
                count = paged.Count,
                totalCount = results.Count,
                offset,
                limit,
                hasMore,
                usagesByKind = grouped,
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "get_namespace_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Build namespace dependency graphs."),
     Description("Get the namespace dependency graph and detect circular namespace dependencies in the solution")]
    public static Task<string> GetNamespaceDependencies(
        IWorkspaceExecutionGate gate,
        INamespaceDependencyService namespaceDependencyService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("When true, return only namespaces and edges involved in circular dependencies")] bool circularOnly = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await namespaceDependencyService.GetNamespaceDependenciesAsync(workspaceId, projectName, c);

            if (circularOnly && result.CircularDependencies.Count > 0)
            {
                var cyclicNamespaces = new HashSet<string>(
                    result.CircularDependencies.SelectMany(cd => cd.Cycle),
                    StringComparer.Ordinal);

                // `with` preserves AnalyzedProjectCount + TotalNamespacesScanned so callers can
                // still see "we analyzed N projects" even when the filtered Nodes/Edges shrink
                // to the cycle subset.
                result = result with
                {
                    Nodes = result.Nodes.Where(n => cyclicNamespaces.Contains(n.Namespace)).ToList(),
                    Edges = result.Edges.Where(e => cyclicNamespaces.Contains(e.FromNamespace) &&
                                                    cyclicNamespaces.Contains(e.ToNamespace)).ToList(),
                };
            }
            else if (circularOnly)
            {
                // No cycles found — drop Nodes/Edges but retain the analysis-coverage counts so
                // the caller can distinguish "no cycles across N projects" from "not analyzed".
                result = result with { Nodes = [], Edges = [] };
            }

            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "get_nuget_dependencies", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Inspect NuGet package references and versions."),
     Description("List all NuGet package references across projects in the workspace, including which projects use each package. Pass `summary=true` to collapse the response to per-package counts + distinct version count — required on multi-project solutions where the default response exceeds the MCP cap (Jellyfin's 40-project graph: ~102 KB).")]
    public static Task<string> GetNuGetDependencies(
        IWorkspaceExecutionGate gate,
        INuGetDependencyService nuGetDependencyService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, returns a compact per-package summary `{packageId, version, projectCount, distinctVersionCount}` instead of the full per-project graph. Default false preserves the verbose shape.")] bool summary = false,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => nuGetDependencyService.GetNuGetDependenciesAsync(workspaceId, c, summary),
            ct);

    [McpServerTool(Name = "find_dead_locals", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "experimental", true, false,
        "Find method-local variables whose only write is not followed by any read."),
     Description("Find method-local variables whose only write is not followed by any read — the class of waste IDE0059 (\"Unnecessary assignment of a value\") covers when the diagnostic is at default severity. Walks every method-like body (methods, constructors, accessors, local functions) and runs SemanticModel.AnalyzeDataFlow once per body, collecting ILocalSymbols that appear in WrittenInside but not in ReadInside. Conservative exclusions: discards (`_`), `foreach` iteration variables, `using`/`await using` resource locals, `catch (Exception ex)` exception locals, pattern-matching designations (`is T x`, `var p`), tuple-deconstruction designations (`var (_, b) = Foo()`), and `out var` declarations at call sites are NOT flagged — those shapes routinely require a name even when the value is unused, and IDE0059 separately suggests the `out _` rewrite. `const` locals are also skipped (removing them changes nameof shape).")]
    public static Task<string> FindDeadLocals(
        IWorkspaceExecutionGate gate,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name to scope the scan on large solutions.")] string? projectFilter = null,
        [Description("Maximum number of hits to return (default: 50).")] int limit = 50,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await unusedCodeAnalyzer.FindDeadLocalsAsync(
                workspaceId,
                new DeadLocalsAnalysisOptions
                {
                    ProjectFilter = projectFilter,
                    Limit = limit
                },
                c);
            return JsonSerializer.Serialize(new { count = results.Count, deadLocals = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_dead_fields", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "experimental", true, false,
        "Find source-declared fields that are never read, never written, or never either."),
     Description("Find source-declared fields whose in-solution usage is incomplete: `never-read`, `never-written`, or `never-either`. Classification uses Roslyn reference finding plus declaration initializers (which count as writes). Skips enum members, constants, compiler-generated backing fields, field-like event storage, and generated files. By default excludes public/protected fields because external consumers may legitimately read or write them; set `includePublic=true` to include that surface. Optional `usageKind` filter accepts `never-read`, `never-written`, or `never-either`. Each hit also includes `removalBlockedBy` (non-null list of `Kind@Path:Line:Col` markers, e.g. `ConstructorWrite@...`, when residual references exist) and `safelyRemovable` (false when `remove_dead_code_preview` would refuse with \"still has references\" — typically DI-captured fields written only in the constructor). Skip chaining `remove_dead_code_preview` on hits where `safelyRemovable=false`.")]
    public static Task<string> FindDeadFields(
        IWorkspaceExecutionGate gate,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name to scope the scan on large solutions.")] string? projectFilter = null,
        [Description("Include public/protected fields in the scan (default: false).")] bool includePublic = false,
        [Description("Optional: restrict results to one usage kind: `never-read`, `never-written`, or `never-either`. Default: all kinds.")] string? usageKind = null,
        [Description("Maximum number of hits to return (default: 50).")] int limit = 50,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await unusedCodeAnalyzer.FindDeadFieldsAsync(
                workspaceId,
                new DeadFieldsAnalysisOptions
                {
                    ProjectFilter = projectFilter,
                    IncludePublic = includePublic,
                    UsageKindFilter = usageKind,
                    Limit = limit
                },
                c);
            return JsonSerializer.Serialize(new { count = results.Count, deadFields = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_duplicate_helpers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "experimental", true, false,
        "Flag private/internal helper methods whose body duplicates a reachable BCL/NuGet symbol."),
     Description("Find private/internal static helpers (on `static class` hosts) whose body is a single ≤ 2-statement delegation to a method declared in a non-source assembly (BCL or referenced NuGet) — the \"reinvented `string.IsNullOrWhiteSpace` / `ArgumentNullException.ThrowIfNull`\" pattern that `find_unused_symbols` misses because the helper IS used locally. Conservative: expression-bodied forwarders and `{ return Target(...); }` bodies return Confidence=high; a single null-guard followed by the delegation returns Confidence=medium. By default, thin forwarders into ASP.NET Core HTTP (`Microsoft.AspNetCore.*`, e.g. `Results.Ok`) and `System.Net.Http` (`HttpClient` helpers) are omitted as framework glue rather than redundant primitives. Set `excludeFrameworkWrappers=false` to include those. Any body that calls the solution's own code (same-solution assembly), or does more than a pure re-wrap, is not flagged. Intentionally distinct from `find_duplicated_methods` (which buckets internal-to-internal structural duplicates); this tool targets internal-vs-referenced-library duplicates.")]
    public static Task<string> FindDuplicateHelpers(
        IWorkspaceExecutionGate gate,
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name to scope the scan on large solutions.")] string? projectFilter = null,
        [Description("Maximum number of hits to return (default: 50).")] int limit = 50,
        [Description("When true (default), omit delegations into Microsoft.AspNetCore.* and System.Net.Http* as framework glue (minimal APIs, HTTP client helpers).")] bool excludeFrameworkWrappers = true,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await unusedCodeAnalyzer.FindDuplicateHelpersAsync(
                workspaceId,
                new DuplicateHelperAnalysisOptions
                {
                    ProjectFilter = projectFilter,
                    Limit = limit,
                    ExcludeFrameworkWrappers = excludeFrameworkWrappers
                },
                c);
            return JsonSerializer.Serialize(new { count = results.Count, helpers = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "find_duplicated_methods", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Find clusters of near-duplicate method bodies by AST-normalized hash."),
     Description("Find clusters of method bodies whose AST-normalized structure is identical (or very close) — surfaces internal copy-paste that should be extracted to a shared helper. Normalization strips trivia, renames locals/parameters to ordinal placeholders, and compares the canonical SyntaxKind sequence, so cosmetic differences (formatting, local names, parameter names) don't affect bucketing. Overloads with identical bodies cluster; overloads with different bodies do not (bucketing is by body-shape, not method name). Auto-generated files (.g.cs, .Designer.cs, obj/), abstract declarations, and partial methods without bodies are excluded. Tune `minLines` up to reduce noise (default 10); narrow `projectFilter` for large solutions. `similarityThreshold` gates exact-structural matches only in the current implementation — near-miss bucketing is reserved for a future iteration, so any value in [0,1] behaves the same as 1.0. Response shape: { count, groups, deprecation } — deprecation is null on the canonical tool and populated on aliases (e.g. find_duplicated_code).")]
    public static Task<string> FindDuplicatedMethods(
        IWorkspaceExecutionGate gate,
        IDuplicateMethodDetectorService duplicateMethodDetectorService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Minimum body line count for a method to be considered (default: 10). Lower values produce more false-positive clusters.")] int minLines = 10,
        [Description("Structural similarity threshold in [0.0, 1.0] (default: 0.85). Exact structural duplicates score 1.0; the current implementation reports only exact-structural matches, so any value <= 1.0 behaves identically.")] double similarityThreshold = 0.85,
        [Description("Optional: filter by project name to scope the scan on large solutions.")] string? projectFilter = null,
        [Description("Maximum number of groups to return (default: 50).")] int limit = 50,
        CancellationToken ct = default)
    {
        return FindDuplicatedMethodsCore(gate, duplicateMethodDetectorService, workspaceId, minLines, similarityThreshold, projectFilter, limit, deprecation: null, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: shared core invoked by both the canonical
    // `find_duplicated_methods` tool and the `find_duplicated_code` alias.
    internal static Task<string> FindDuplicatedMethodsCore(
        IWorkspaceExecutionGate gate,
        IDuplicateMethodDetectorService duplicateMethodDetectorService,
        string workspaceId,
        int minLines,
        double similarityThreshold,
        string? projectFilter,
        int limit,
        ToolAliasDeprecation? deprecation,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await duplicateMethodDetectorService.FindDuplicatedMethodsAsync(
                workspaceId,
                new DuplicateMethodAnalysisOptions
                {
                    MinLines = minLines,
                    SimilarityThreshold = similarityThreshold,
                    ProjectFilter = projectFilter,
                    Limit = limit
                },
                c);
            return JsonSerializer.Serialize(new { count = results.Count, groups = results, deprecation }, JsonDefaults.Indented);
        }, ct);
    }

    // roslyn-mcp-sister-tool-name-aliases: thin alias for callers carrying the python-refactor
    // (Jedi) tool name `find_duplicated_code`. The plan-document evidence cites both
    // `find_duplicated_methods` and `find_duplicate_helpers` as candidate canonicals; the
    // broader semantic match is `find_duplicated_methods` (clusters of internal copy-paste),
    // so the alias delegates there. Callers wanting the BCL/NuGet-wrapper detection should
    // call `find_duplicate_helpers` directly.
    [McpServerTool(Name = "find_duplicated_code", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Alias for find_duplicated_methods (cross-MCP-server name compatibility)."),
     Description("Alias for `find_duplicated_methods` (cross-MCP-server name compatibility — matches the python-refactor tool name). Returns the canonical find_duplicated_methods response envelope ({ count, groups, deprecation }) with deprecation.canonicalName populated. Note: the python-refactor server has only one duplicate-detection tool while this server has two — `find_duplicated_methods` (clusters of internal copy-paste, picked here as the broader match) and `find_duplicate_helpers` (private/internal helpers that re-wrap a BCL/NuGet symbol). Call those canonicals directly when you need the targeted shape.")]
    public static Task<string> FindDuplicatedCode(
        IWorkspaceExecutionGate gate,
        IDuplicateMethodDetectorService duplicateMethodDetectorService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Minimum body line count for a method to be considered (default: 10).")] int minLines = 10,
        [Description("Structural similarity threshold in [0.0, 1.0] (default: 0.85).")] double similarityThreshold = 0.85,
        [Description("Optional: filter by project name to scope the scan on large solutions.")] string? projectFilter = null,
        [Description("Maximum number of groups to return (default: 50).")] int limit = 50,
        CancellationToken ct = default)
    {
        return FindDuplicatedMethodsCore(
            gate,
            duplicateMethodDetectorService,
            workspaceId,
            minLines,
            similarityThreshold,
            projectFilter,
            limit,
            ToolAliasDeprecation.ForSisterAlias("find_duplicated_methods"),
            ct);
    }

    [McpServerTool(Name = "semantic_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Run semantic search over symbols and declarations."),
     Description("Search for symbols by semantic criteria. NOTE: this tool does NOT use embedding-based similarity or vector search — matching is done via structured Roslyn predicate parsing (symbol kind, modifiers, return types, etc.) with a token-substring fallback for queries that do not parse to a structured predicate. Supports natural language queries like 'async methods returning Task<bool>', 'classes implementing IDisposable', 'methods with more than 5 parameters', 'static methods', 'virtual properties', 'generic classes', etc. async gotcha: the 'async' keyword maps to Roslyn's IMethodSymbol.IsAsync which REQUIRES the 'async' modifier on the declaration — a Task<T>-returning method that uses Task.FromResult(...) without 'async' is NOT matched. Query 'methods returning Task<bool>' without 'async' to match all Task-returning methods. Verbose-query fallback: long natural-language queries that fail structured parsing decompose into stopword-filtered tokens and match any symbol name containing a token; the response Debug payload shows the parsed tokens, applied predicates, and fallback strategy (structured/name-substring/token-or-match/none) so callers can see why a query matched or missed.")]
    public static Task<string> SemanticSearch(
        IWorkspaceExecutionGate gate,
        ICodePatternAnalyzer codePatternAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Semantic search query, e.g. 'async methods returning Task<bool>', 'classes implementing IDisposable', 'public static methods'")] string query,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Maximum number of results to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var response = await codePatternAnalyzer.SemanticSearchAsync(workspaceId, query, projectName, limit, c);
            // semantic-search-duplicate-results-and-fallback-signal: project the Debug
            // payload (parsedTokens / appliedPredicates / fallbackStrategy) so callers
            // can decode why a verbose natural-language query landed on a particular
            // strategy. Per-result MatchKind is part of the result rows themselves.
            return JsonSerializer.Serialize(new
            {
                count = response.Results.Count,
                results = response.Results,
                warning = response.Warning,
                debug = response.Debug
            }, JsonDefaults.Indented);
        }, ct);
    }
}
