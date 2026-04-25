namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Static inventory of all MCP tools, resources, and prompts exposed by the server,
/// together with their support tiers, categories, and read/destructive flags.
/// </summary>
public static partial class ServerSurfaceCatalog
{
    public const string CatalogVersion = "2026.04";

    private static readonly Lazy<IReadOnlyList<SurfaceEntry>> s_allTools = new(
        static () => [..WorkspaceTools!, ..SymbolTools!, ..AnalysisTools!, ..RefactoringTools!, ..EditingTools!, ..OrchestrationTools!]);

    public static IReadOnlyList<SurfaceEntry> Tools => s_allTools.Value;

    public static SurfaceSummary GetSummary()
    {
        return new SurfaceSummary(
            CatalogVersion,
            CountByTier(Tools, "stable"),
            CountByTier(Tools, "experimental"),
            CountByTier(Resources, "stable"),
            CountByTier(Resources, "experimental"),
            CountByTier(Prompts, "stable"),
            CountByTier(Prompts, "experimental"));
    }

    public static IReadOnlyList<WorkflowHint> WorkflowHints { get; } =
    [
        new("Preview/Apply", ["rename_preview", "rename_apply"], "Most write operations use a two-step pattern: call *_preview to inspect the diff, then *_apply with the preview token."),
        new("Diagnostic Fix", ["project_diagnostics", "diagnostic_details", "code_fix_preview", "code_fix_apply"], "Identify diagnostics, inspect details, preview a fix, then apply it."),
        new("Code Action", ["get_code_actions", "preview_code_action", "apply_code_action"], "List available Roslyn refactorings/fixes at a location, preview, then apply."),
        new("Security Audit", ["security_analyzer_status", "security_diagnostics", "diagnostic_details", "code_fix_preview", "code_fix_apply"], "Check analyzer coverage, get security findings, then fix them."),
        new("Build and Test", ["build_workspace", "test_run"], "Validate compilation then run tests after any code change."),
        new("Change Validation", ["test_related_files", "test_run"], "Find tests related to changed files, then run them."),
        new("Post-edit Verify (auto-scoped)", ["validate_recent_git_changes"], "Preferred post-edit bundle: derives changedFilePaths from git status --porcelain and runs compile_check + project_diagnostics + test_related_files scoped to the touched-file set. Falls back to validate_workspace scope with a warning when git is unavailable."),
        new("Dead Code Cleanup", ["find_unused_symbols", "remove_dead_code_preview", "remove_dead_code_apply", "build_workspace"], "Find unused symbols, preview removal, apply, then verify build."),
        new("Package Migration", ["migrate_package_preview", "apply_composite_preview", "build_workspace"], "Preview migrating a package across projects, apply, then verify."),
        new("Coverage Analysis", ["test_discover", "test_coverage", "scaffold_test_preview", "scaffold_test_apply"], "Discover tests, run coverage, scaffold new tests for gaps."),
        new("SRP Analysis & Type Extraction", ["get_cohesion_metrics", "find_shared_members", "extract_type_preview", "extract_type_apply", "build_workspace"], "Identify types with multiple responsibilities via LCOM4 metrics, find shared private members that complicate extraction, preview and apply the extraction, then verify the build."),
        new("Consumer Impact Analysis", ["find_consumers", "find_references_bulk", "impact_analysis"], "Find all types that depend on a type or interface, understand dependency kinds (constructor, field, parameter, base type), then assess change impact."),
        new("Interface Extraction & Migration", ["extract_interface_preview", "extract_interface_apply", "bulk_replace_type_preview", "bulk_replace_type_apply", "build_workspace"], "Extract an interface from a concrete type, then bulk-replace all parameter/field references to use the interface instead. Verify with build."),
        new("Type Organization", ["move_type_to_file_preview", "move_type_to_file_apply", "build_workspace"], "Move types from multi-type files into their own dedicated files for better code organization."),
        new("Batch Diagnostic Fix", ["list_analyzers", "project_diagnostics", "fix_all_preview", "fix_all_apply", "build_workspace"], "List available analyzers and rules, identify diagnostics, batch-fix all occurrences across the solution, then verify the build."),
        new("Flow Analysis for Refactoring", ["analyze_data_flow", "analyze_control_flow", "extract_type_preview", "extract_type_apply"], "Analyze data and control flow to understand variable dependencies and reachability before extracting code into new types or methods."),
        new("Quick Validation", ["compile_check", "analyze_snippet", "evaluate_csharp"], "Use in-memory compilation, snippet analysis, or script evaluation for rapid feedback without full builds."),
        new("Selection-Range Refactoring", ["get_code_actions", "preview_code_action", "apply_code_action", "compile_check"], "Pass startLine/startColumn/endLine/endColumn to get_code_actions for selection-based refactorings: introduce parameter (expression → method parameter with call-site updates) and inline temporary variable. Preview, apply, then verify compilation.")
    ];

    /// <summary>
    /// dr-9-11-payload-exceeds-mcp-tool-result-cap: full catalog including every tool. The
    /// paginated resource variants (<c>roslyn://server/catalog</c> default + per-page sibling)
    /// deliver this same material in cap-safe chunks; this method remains the canonical
    /// programmatic entry point for in-process consumers (surface parity tests, internal docs
    /// generation).
    /// </summary>
    public static ServerCatalogDto CreateDocument()
    {
        return new ServerCatalogDto(
            CatalogVersion,
            ProductShape: "local-first",
            SupportPolicy: "Stable entries are covered by release documentation and compatibility expectations for the local stdio host. Experimental entries may evolve faster and can change with less notice before a remote host exists.",
            ProductBoundaries:
            [
                "The production target is the local stdio server running on a developer workstation.",
                "Workspace state comes from MSBuildWorkspace and on-disk files, not unsaved editor buffers.",
                "Future HTTP/SSE hosting is intentionally a separate operational tier and not part of the current stable contract.",
                "Write-capable tools require an already loaded workspace and are intentionally bounded by preview/apply or explicit edit requests."
            ],
            Tools,
            Resources,
            Prompts,
            WorkflowHints,
            Summary: GetSummary());
    }

    /// <summary>
    /// dr-9-11-payload-exceeds-mcp-tool-result-cap: cap-safe summary document served by
    /// <c>roslyn://server/catalog</c>. Tools and prompts dominate the full payload (~80 KB on
    /// a 168-tool solution); this shape drops both lists and replaces them with pagination
    /// pointers and totals. Resources stay inline (10 entries, fits easily). Callers that
    /// need tool/prompt rows fetch the paginated siblings.
    /// </summary>
    public static ServerCatalogSummaryDto CreateSummaryDocument()
    {
        return new ServerCatalogSummaryDto(
            CatalogVersion,
            ProductShape: "local-first",
            SupportPolicy: "Stable entries are covered by release documentation and compatibility expectations for the local stdio host. Experimental entries may evolve faster and can change with less notice before a remote host exists.",
            ProductBoundaries:
            [
                "The production target is the local stdio server running on a developer workstation.",
                "Workspace state comes from MSBuildWorkspace and on-disk files, not unsaved editor buffers.",
                "Future HTTP/SSE hosting is intentionally a separate operational tier and not part of the current stable contract.",
                "Write-capable tools require an already loaded workspace and are intentionally bounded by preview/apply or explicit edit requests."
            ],
            ToolCount: Tools.Count,
            ToolsResourceTemplate: "roslyn://server/catalog/tools/{offset}/{limit}",
            Resources,
            PromptCount: Prompts.Count,
            PromptsResourceTemplate: "roslyn://server/catalog/prompts/{offset}/{limit}",
            WorkflowHints,
            Summary: GetSummary());
    }

    /// <summary>
    /// Return a paginated slice of an entry list. Offset clamps to [0, entries.Count]; limit
    /// clamps to [1, 200]. Response carries the page window + totals + hasMore so callers
    /// can iterate without guessing.
    /// </summary>
    public static ServerCatalogPagedEntriesDto PageEntries(
        IReadOnlyList<SurfaceEntry> entries, int offset, int limit, string resourceName)
    {
        var total = entries.Count;
        var clampedOffset = Math.Clamp(offset, 0, total);
        var clampedLimit = Math.Clamp(limit, 1, 200);
        var remaining = total - clampedOffset;
        var take = Math.Min(clampedLimit, remaining);
        var page = take <= 0
            ? Array.Empty<SurfaceEntry>()
            : entries.Skip(clampedOffset).Take(take).ToArray();

        return new ServerCatalogPagedEntriesDto(
            ResourceName: resourceName,
            Offset: clampedOffset,
            Limit: clampedLimit,
            ReturnedCount: page.Length,
            TotalCount: total,
            HasMore: clampedOffset + page.Length < total,
            Entries: page);
    }

    private static int CountByTier(IReadOnlyList<SurfaceEntry> entries, string tier) =>
        entries.Count(entry => string.Equals(entry.SupportTier, tier, StringComparison.Ordinal));

    private static SurfaceEntry Tool(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("tool", name, category, supportTier, readOnly, destructive, summary, UriTemplate: null, Parameters: null);

    private static SurfaceEntry Resource(string name, string category, string supportTier, bool readOnly, bool destructive, string summary, string uriTemplate) =>
        new("resource", name, category, supportTier, readOnly, destructive, summary, uriTemplate, Parameters: null);

    // get-prompt-text-publish-parameter-schema: the prompt factory always splices the
    // PromptParameterIndex's reflected parameter list onto the entry. Reflection runs once at
    // type init (Lazy<>), so populating per-row here is O(1) lookup after the first hit.
    private static SurfaceEntry Prompt(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("prompt", name, category, supportTier, readOnly, destructive, summary,
            UriTemplate: null,
            Parameters: PromptParameterIndex.GetParameters(name));
}

/// <summary>
/// Describes a single tool, resource, or prompt entry in the server surface catalog.
/// </summary>
/// <param name="Kind">The entry kind: <c>tool</c>, <c>resource</c>, or <c>prompt</c>.</param>
/// <param name="Name">The tool/resource/prompt name as used by MCP clients.</param>
/// <param name="Category">The logical grouping category (e.g., <c>symbols</c>, <c>refactoring</c>).</param>
/// <param name="SupportTier">Either <c>stable</c> or <c>experimental</c>.</param>
/// <param name="ReadOnly">When <see langword="true"/>, the entry does not modify workspace state.</param>
/// <param name="Destructive">When <see langword="true"/>, the entry performs an irreversible or high-impact change.</param>
/// <param name="Summary">A short human-readable description of what the entry does.</param>
/// <param name="UriTemplate">The URI template for resource entries, or <see langword="null"/> for tools and prompts.</param>
/// <param name="Parameters">
/// get-prompt-text-publish-parameter-schema: the user-facing parameter schema for prompt entries
/// (<see cref="ServerSurfaceCatalog.Prompts"/>); <see langword="null"/> for tools and resources.
/// Excludes DI-resolved services and <see cref="CancellationToken"/> — only the values an agent
/// must supply via <c>parametersJson</c> on <c>get_prompt_text</c>.
/// </param>
public sealed record SurfaceEntry(
    string Kind,
    string Name,
    string Category,
    string SupportTier,
    bool ReadOnly,
    bool Destructive,
    string Summary,
    string? UriTemplate,
    IReadOnlyList<PromptParameterEntry>? Parameters);

/// <summary>
/// get-prompt-text-publish-parameter-schema: per-parameter schema row published on each
/// prompt entry in <c>roslyn://server/catalog/prompts/{offset}/{limit}</c>. Lets callers
/// build the <c>parametersJson</c> argument for <c>get_prompt_text</c> without a failing
/// invocation to discover the signature.
/// </summary>
/// <param name="Name">The parameter name, matching the prompt method's parameter symbol.</param>
/// <param name="Type">A C#-style type label (e.g. <c>string</c>, <c>int?</c>, <c>List&lt;string&gt;</c>).</param>
/// <param name="Required">When <see langword="true"/>, the parameter has no default and MUST be supplied.</param>
/// <param name="DefaultValue">The default value for optional parameters, or <see langword="null"/> when required (or when the default is itself <see langword="null"/>).</param>
/// <param name="Description">The <see cref="System.ComponentModel.DescriptionAttribute"/> text on the parameter, when present.</param>
public sealed record PromptParameterEntry(
    string Name,
    string Type,
    bool Required,
    object? DefaultValue,
    string? Description);

/// <summary>
/// Provides a count summary of stable and experimental entries for each surface kind.
/// </summary>
public sealed record SurfaceSummary(
    string CatalogVersion,
    int StableTools,
    int ExperimentalTools,
    int StableResources,
    int ExperimentalResources,
    int StablePrompts,
    int ExperimentalPrompts);

/// <summary>
/// The machine-readable server catalog document returned by the <c>server_catalog</c> resource.
/// </summary>
public sealed record ServerCatalogDto(
    string CatalogVersion,
    string ProductShape,
    string SupportPolicy,
    IReadOnlyList<string> ProductBoundaries,
    IReadOnlyList<SurfaceEntry> Tools,
    IReadOnlyList<SurfaceEntry> Resources,
    IReadOnlyList<SurfaceEntry> Prompts,
    IReadOnlyList<WorkflowHint> WorkflowHints,
    SurfaceSummary Summary);

/// <summary>
/// dr-9-11-payload-exceeds-mcp-tool-result-cap: cap-safe summary returned by the default
/// <c>roslyn://server/catalog</c> resource. Tool and prompt lists are replaced with a count +
/// paginated-sibling URI template; resources stay inline because the list is small.
/// </summary>
public sealed record ServerCatalogSummaryDto(
    string CatalogVersion,
    string ProductShape,
    string SupportPolicy,
    IReadOnlyList<string> ProductBoundaries,
    int ToolCount,
    string ToolsResourceTemplate,
    IReadOnlyList<SurfaceEntry> Resources,
    int PromptCount,
    string PromptsResourceTemplate,
    IReadOnlyList<WorkflowHint> WorkflowHints,
    SurfaceSummary Summary);

/// <summary>
/// dr-9-11-payload-exceeds-mcp-tool-result-cap: response shape for a paginated slice of
/// surface entries (tools or prompts). Matches the pagination contract used by the tools
/// (offset/limit/returned/total/hasMore) so clients can iterate uniformly.
/// </summary>
public sealed record ServerCatalogPagedEntriesDto(
    string ResourceName,
    int Offset,
    int Limit,
    int ReturnedCount,
    int TotalCount,
    bool HasMore,
    IReadOnlyList<SurfaceEntry> Entries);

/// <summary>
/// Describes a common tool workflow — a sequence of tools that work together.
/// </summary>
/// <param name="Name">Human-readable workflow name (e.g., "Diagnostic Fix").</param>
/// <param name="ToolSequence">Ordered list of tool names in the typical execution flow.</param>
/// <param name="Description">Short description of when and how to use this workflow.</param>
public sealed record WorkflowHint(
    string Name,
    IReadOnlyList<string> ToolSequence,
    string Description);
