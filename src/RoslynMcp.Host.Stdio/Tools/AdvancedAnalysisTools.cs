using System.ComponentModel;
using System.Text.Json;
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
     Description("Scan the solution for dependency injection registrations (AddSingleton, AddScoped, AddTransient) and return the service-to-implementation mappings. Pass showLifetimeOverrides=true to additionally emit per-service-type override chains (winning lifetime, lifetime-mismatch flag, dead-registration count) — opt-in to keep the default payload shape stable.")]
    public static Task<string> GetDiRegistrations(
        IWorkspaceExecutionGate gate,
        IDiRegistrationService diRegistrationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("When true, also emit overrideChains[] grouping registrations by service type with the winning lifetime, lifetime-mismatch flag (Singleton vs Scoped vs Transient), and dead-registration count. Default: false (legacy shape: count + registrations[]).")] bool showLifetimeOverrides = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            if (!showLifetimeOverrides)
            {
                var results = await diRegistrationService.GetDiRegistrationsAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(new { count = results.Count, registrations = results }, JsonDefaults.Indented);
            }

            // di-lifetime-mismatch-detection: opt-in path returns the legacy registrations
            // list (unchanged shape) plus the per-service-type override chains.
            var scan = await diRegistrationService.GetDiRegistrationsWithOverridesAsync(workspaceId, projectName, c);
            return JsonSerializer.Serialize(new
            {
                count = scan.Registrations.Count,
                registrations = scan.Registrations,
                overrideChainCount = scan.OverrideChains.Count,
                overrideChains = scan.OverrideChains,
            }, JsonDefaults.Indented);
        }, ct);
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
        [Description("Optional: list of source file paths to include (union with filePath). Empty list means no filter. Useful for re-running complexity on a changed-file set after a refactor.")] IReadOnlyList<string>? filePaths = null,
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
     Description("Find all reflection API usage in the solution (typeof, Type.GetMethod, Activator.CreateInstance, Assembly.Load, etc.)")]
    public static Task<string> FindReflectionUsages(
        IWorkspaceExecutionGate gate,
        ICodePatternAnalyzer codePatternAnalyzer,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var results = await codePatternAnalyzer.FindReflectionUsagesAsync(workspaceId, projectName, c);
            var grouped = results.GroupBy(r => r.UsageKind)
                .ToDictionary(g => g.Key, g => g.ToList());
            return JsonSerializer.Serialize(new { count = results.Count, usagesByKind = grouped }, JsonDefaults.Indented);
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

                result = new RoslynMcp.Core.Models.NamespaceDependencyGraphDto(
                    result.Nodes.Where(n => cyclicNamespaces.Contains(n.Namespace)).ToList(),
                    result.Edges.Where(e => cyclicNamespaces.Contains(e.FromNamespace) &&
                                            cyclicNamespaces.Contains(e.ToNamespace)).ToList(),
                    result.CircularDependencies);
            }
            else if (circularOnly)
            {
                result = new RoslynMcp.Core.Models.NamespaceDependencyGraphDto([], [], []);
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
     Description("Find source-declared fields whose in-solution usage is incomplete: `never-read`, `never-written`, or `never-either`. Classification uses Roslyn reference finding plus declaration initializers (which count as writes). Skips enum members, constants, compiler-generated backing fields, field-like event storage, and generated files. By default excludes public/protected fields because external consumers may legitimately read or write them; set `includePublic=true` to include that surface. Optional `usageKind` filter accepts `never-read`, `never-written`, or `never-either`.")]
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
     Description("Find clusters of method bodies whose AST-normalized structure is identical (or very close) — surfaces internal copy-paste that should be extracted to a shared helper. Normalization strips trivia, renames locals/parameters to ordinal placeholders, and compares the canonical SyntaxKind sequence, so cosmetic differences (formatting, local names, parameter names) don't affect bucketing. Overloads with identical bodies cluster; overloads with different bodies do not (bucketing is by body-shape, not method name). Auto-generated files (.g.cs, .Designer.cs, obj/), abstract declarations, and partial methods without bodies are excluded. Tune `minLines` up to reduce noise (default 10); narrow `projectFilter` for large solutions. `similarityThreshold` gates exact-structural matches only in the current implementation — near-miss bucketing is reserved for a future iteration, so any value in [0,1] behaves the same as 1.0.")]
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
            return JsonSerializer.Serialize(new { count = results.Count, groups = results }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "semantic_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Run semantic search over symbols and declarations."),
     Description("Search for symbols by semantic criteria. Supports natural language queries like 'async methods returning Task<bool>', 'classes implementing IDisposable', 'methods with more than 5 parameters', 'static methods', 'virtual properties', 'generic classes', etc. async gotcha: the 'async' keyword maps to Roslyn's IMethodSymbol.IsAsync which REQUIRES the 'async' modifier on the declaration — a Task<T>-returning method that uses Task.FromResult(...) without 'async' is NOT matched. Query 'methods returning Task<bool>' without 'async' to match all Task-returning methods. Verbose-query fallback: long natural-language queries that fail structured parsing decompose into stopword-filtered tokens and match any symbol name containing a token; the response Debug payload shows the parsed tokens, applied predicates, and fallback strategy (structured/name-substring/token-or-match/none) so callers can see why a query matched or missed.")]
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
            return JsonSerializer.Serialize(new
            {
                count = response.Results.Count,
                results = response.Results,
                warning = response.Warning
            }, JsonDefaults.Indented);
        }, ct);
    }
}
