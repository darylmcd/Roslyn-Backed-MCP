namespace Company.RoslynMcp.Host.Stdio.Catalog;

public static class ServerSurfaceCatalog
{
    public const string CatalogVersion = "2026.03";

    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
    [
        Tool("server_info", "server", "stable", true, false, "Inspect server capabilities, versions, and support tiers."),

        Tool("workspace_load", "workspace", "stable", false, false, "Load a solution or project into a named Roslyn workspace session."),
        Tool("workspace_reload", "workspace", "stable", false, false, "Reload an existing workspace session from disk."),
        Tool("workspace_close", "workspace", "stable", false, true, "Close a loaded workspace session and release resources."),
        Tool("workspace_list", "workspace", "stable", true, false, "List active workspace sessions."),
        Tool("workspace_status", "workspace", "stable", true, false, "Inspect status, diagnostics, and stale-state information for a workspace."),
        Tool("project_graph", "workspace", "stable", true, false, "Inspect project and dependency structure."),
        Tool("source_generated_documents", "workspace", "stable", true, false, "List source-generated documents for a workspace or project."),
        Tool("get_source_text", "workspace", "stable", true, false, "Read source text as Roslyn currently sees it in the loaded workspace."),

        Tool("symbol_search", "symbols", "stable", true, false, "Search symbols by name across the workspace."),
        Tool("symbol_info", "symbols", "stable", true, false, "Inspect the symbol at a source location."),
        Tool("go_to_definition", "symbols", "stable", true, false, "Navigate to the symbol definition."),
        Tool("find_references", "symbols", "stable", true, false, "Find references to a symbol."),
        Tool("find_implementations", "symbols", "stable", true, false, "Find implementations of an interface or abstract member."),
        Tool("document_symbols", "symbols", "stable", true, false, "List declared symbols in a document."),
        Tool("find_overrides", "symbols", "stable", true, false, "Find overrides of a virtual or abstract member."),
        Tool("find_base_members", "symbols", "stable", true, false, "Find base or implemented members."),
        Tool("member_hierarchy", "symbols", "stable", true, false, "Summarize base and override relationships for a member."),
        Tool("symbol_signature_help", "symbols", "stable", true, false, "Return symbol signature and documentation."),
        Tool("symbol_relationships", "symbols", "stable", true, false, "Combine definition, reference, base, and implementation relationships."),
        Tool("find_references_bulk", "symbols", "stable", true, false, "Resolve references for multiple symbols in one request."),
        Tool("find_property_writes", "symbols", "stable", true, false, "Find property write sites and classify object-initializer writes."),
        Tool("enclosing_symbol", "symbols", "stable", true, false, "Return the enclosing symbol for a source position."),
        Tool("goto_type_definition", "symbols", "stable", true, false, "Navigate from a symbol usage to its type definition."),
        Tool("get_completions", "symbols", "stable", true, false, "Return IntelliSense-style completion items at a position."),

        Tool("project_diagnostics", "analysis", "stable", true, false, "Return compiler diagnostics for a workspace."),
        Tool("diagnostic_details", "analysis", "stable", true, false, "Inspect one diagnostic occurrence in detail."),
        Tool("type_hierarchy", "analysis", "stable", true, false, "Inspect type inheritance and interface relationships."),
        Tool("callers_callees", "analysis", "stable", true, false, "Find direct callers and callees for a method."),
        Tool("impact_analysis", "analysis", "stable", true, false, "Estimate the impact of changing a symbol."),
        Tool("find_type_mutations", "analysis", "stable", true, false, "Identify mutating members of a type and who calls them."),
        Tool("find_type_usages", "analysis", "stable", true, false, "Classify usages of a type across the solution."),

        Tool("build_workspace", "validation", "stable", false, false, "Run dotnet build for the loaded workspace."),
        Tool("build_project", "validation", "stable", false, false, "Run dotnet build for a selected project."),
        Tool("test_discover", "validation", "stable", true, false, "Discover tests in the loaded workspace."),
        Tool("test_run", "validation", "stable", false, false, "Run dotnet test for the workspace or a selected project."),
        Tool("test_related", "validation", "stable", true, false, "Find tests related to a symbol."),
        Tool("test_related_files", "validation", "stable", true, false, "Find tests related to a set of changed files."),

        Tool("rename_preview", "refactoring", "stable", true, false, "Preview a rename refactoring."),
        Tool("rename_apply", "refactoring", "stable", false, true, "Apply a previously previewed rename refactoring."),
        Tool("organize_usings_preview", "refactoring", "stable", true, false, "Preview using-directive cleanup."),
        Tool("organize_usings_apply", "refactoring", "stable", false, true, "Apply a previously previewed organize-usings operation."),
        Tool("format_document_preview", "refactoring", "stable", true, false, "Preview document formatting."),
        Tool("format_document_apply", "refactoring", "stable", false, true, "Apply a previously previewed document format operation."),
        Tool("code_fix_preview", "refactoring", "stable", true, false, "Preview a curated diagnostic code fix."),
        Tool("code_fix_apply", "refactoring", "stable", false, true, "Apply a previously previewed curated code fix."),

        Tool("find_unused_symbols", "advanced-analysis", "experimental", true, false, "Find likely unused symbols."),
        Tool("get_di_registrations", "advanced-analysis", "experimental", true, false, "Inspect DI registration patterns in source."),
        Tool("get_complexity_metrics", "advanced-analysis", "experimental", true, false, "Compute cyclomatic complexity and related metrics."),
        Tool("find_reflection_usages", "advanced-analysis", "experimental", true, false, "Find reflection-heavy call sites."),
        Tool("get_namespace_dependencies", "advanced-analysis", "experimental", true, false, "Build namespace dependency graphs."),
        Tool("get_nuget_dependencies", "advanced-analysis", "experimental", true, false, "Inspect NuGet package references and versions."),
        Tool("semantic_search", "advanced-analysis", "experimental", true, false, "Run semantic search over symbols and declarations."),

        Tool("apply_text_edit", "editing", "experimental", false, true, "Apply direct text edits to a single file."),
        Tool("apply_multi_file_edit", "editing", "experimental", false, true, "Apply direct text edits to multiple files."),
        Tool("test_coverage", "validation", "experimental", false, false, "Run coverage collection for test execution."),
        Tool("get_syntax_tree", "syntax", "experimental", true, false, "Return a structured syntax tree for a document or range."),
        Tool("get_code_actions", "code-actions", "experimental", true, false, "List Roslyn code fixes and refactorings at a location."),
        Tool("preview_code_action", "code-actions", "experimental", true, false, "Preview a Roslyn code action before applying it."),
        Tool("apply_code_action", "code-actions", "experimental", false, true, "Apply a previously previewed Roslyn code action.")
    ];

    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
    [
        Resource("server_catalog", "server", "stable", true, false, "Machine-readable support policy and surface inventory.", "roslyn://server/catalog"),
        Resource("workspaces", "workspace", "stable", true, false, "List active workspace sessions.", "roslyn://workspaces"),
        Resource("workspace_status", "workspace", "stable", true, false, "Inspect workspace status and diagnostics.", "roslyn://workspace/{workspaceId}/status"),
        Resource("workspace_projects", "workspace", "stable", true, false, "Read project graph metadata for a workspace.", "roslyn://workspace/{workspaceId}/projects"),
        Resource("workspace_diagnostics", "analysis", "stable", true, false, "Read all compiler diagnostics for a workspace.", "roslyn://workspace/{workspaceId}/diagnostics"),
        Resource("source_file", "workspace", "stable", true, false, "Read a source file from the loaded workspace.", "roslyn://workspace/{workspaceId}/file/{filePath}")
    ];

    public static IReadOnlyList<SurfaceEntry> Prompts { get; } =
    [
        Prompt("explain_error", "prompts", "experimental", true, false, "Generate a prompt for explaining a compiler diagnostic."),
        Prompt("suggest_refactoring", "prompts", "experimental", true, false, "Generate a prompt for refactoring suggestions."),
        Prompt("review_file", "prompts", "experimental", true, false, "Generate a prompt for file review."),
        Prompt("analyze_dependencies", "prompts", "experimental", true, false, "Generate a prompt for architecture and dependency analysis."),
        Prompt("debug_test_failure", "prompts", "experimental", true, false, "Generate a prompt for debugging a failing test.")
    ];

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
            Summary: GetSummary());
    }

    private static int CountByTier(IReadOnlyList<SurfaceEntry> entries, string tier) =>
        entries.Count(entry => string.Equals(entry.SupportTier, tier, StringComparison.Ordinal));

    private static SurfaceEntry Tool(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("tool", name, category, supportTier, readOnly, destructive, summary, null);

    private static SurfaceEntry Resource(string name, string category, string supportTier, bool readOnly, bool destructive, string summary, string uriTemplate) =>
        new("resource", name, category, supportTier, readOnly, destructive, summary, uriTemplate);

    private static SurfaceEntry Prompt(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("prompt", name, category, supportTier, readOnly, destructive, summary, null);
}

public sealed record SurfaceEntry(
    string Kind,
    string Name,
    string Category,
    string SupportTier,
    bool ReadOnly,
    bool Destructive,
    string Summary,
    string? UriTemplate);

public sealed record SurfaceSummary(
    string CatalogVersion,
    int StableTools,
    int ExperimentalTools,
    int StableResources,
    int ExperimentalResources,
    int StablePrompts,
    int ExperimentalPrompts);

public sealed record ServerCatalogDto(
    string CatalogVersion,
    string ProductShape,
    string SupportPolicy,
    IReadOnlyList<string> ProductBoundaries,
    IReadOnlyList<SurfaceEntry> Tools,
    IReadOnlyList<SurfaceEntry> Resources,
    IReadOnlyList<SurfaceEntry> Prompts,
    SurfaceSummary Summary);
