namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Static inventory of all MCP tools, resources, and prompts exposed by the server,
/// together with their support tiers, categories, and read/destructive flags.
/// </summary>
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
        Tool("get_source_text", "workspace", "stable", true, false, "Read source text as the Roslyn workspace currently sees it (may differ from disk if workspace hasn't been reloaded)."),

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
        Tool("create_file_preview", "file-operations", "experimental", true, false, "Preview creating a new source file in a project."),
        Tool("create_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file creation."),
        Tool("delete_file_preview", "file-operations", "experimental", true, false, "Preview deleting an existing source file."),
        Tool("delete_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file deletion."),
        Tool("move_file_preview", "file-operations", "experimental", true, false, "Preview moving a source file, optionally updating its namespace."),
        Tool("move_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file move."),
        Tool("add_package_reference_preview", "project-mutation", "experimental", true, false, "Preview adding a PackageReference to a project file."),
        Tool("remove_package_reference_preview", "project-mutation", "experimental", true, false, "Preview removing a PackageReference from a project file."),
        Tool("add_project_reference_preview", "project-mutation", "experimental", true, false, "Preview adding a ProjectReference to a project file."),
        Tool("remove_project_reference_preview", "project-mutation", "experimental", true, false, "Preview removing a ProjectReference from a project file."),
        Tool("set_project_property_preview", "project-mutation", "experimental", true, false, "Preview setting an allowlisted property in a project file."),
        Tool("add_target_framework_preview", "project-mutation", "experimental", true, false, "Preview adding a target framework to a project file."),
        Tool("remove_target_framework_preview", "project-mutation", "experimental", true, false, "Preview removing a target framework from a project file."),
        Tool("set_conditional_property_preview", "project-mutation", "experimental", true, false, "Preview setting an allowlisted conditional project property."),
        Tool("add_central_package_version_preview", "project-mutation", "experimental", true, false, "Preview adding a PackageVersion entry to Directory.Packages.props."),
        Tool("remove_central_package_version_preview", "project-mutation", "experimental", true, false, "Preview removing a PackageVersion entry from Directory.Packages.props."),
        Tool("apply_project_mutation", "project-mutation", "experimental", false, true, "Apply a previously previewed project file mutation."),
        Tool("scaffold_type_preview", "scaffolding", "experimental", true, false, "Preview scaffolding a new type file in a project."),
        Tool("scaffold_type_apply", "scaffolding", "experimental", false, true, "Apply a previously previewed type scaffolding operation."),
        Tool("scaffold_test_preview", "scaffolding", "experimental", true, false, "Preview scaffolding a new MSTest file for a target type."),
        Tool("scaffold_test_apply", "scaffolding", "experimental", false, true, "Apply a previously previewed test scaffolding operation."),
        Tool("remove_dead_code_preview", "dead-code", "experimental", true, false, "Preview removing unused symbols by handle."),
        Tool("remove_dead_code_apply", "dead-code", "experimental", false, true, "Apply a previously previewed dead-code removal operation."),
        Tool("move_type_to_project_preview", "cross-project-refactoring", "experimental", true, false, "Preview moving a type declaration into another project."),
        Tool("extract_interface_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface from a concrete type."),
        Tool("dependency_inversion_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface and updating constructor dependencies."),
        Tool("migrate_package_preview", "orchestration", "experimental", true, false, "Preview migrating a package across affected projects."),
        Tool("split_class_preview", "orchestration", "experimental", true, false, "Preview splitting a class into a new partial file."),
        Tool("extract_and_wire_interface_preview", "orchestration", "experimental", true, false, "Preview extracting an interface and updating DI registrations."),
        Tool("apply_composite_preview", "orchestration", "experimental", false, true, "Apply a previously previewed orchestration operation."),
        Tool("test_coverage", "validation", "experimental", false, false, "Run coverage collection for test execution."),
        Tool("get_syntax_tree", "syntax", "experimental", true, false, "Return a structured syntax tree for a document or range."),
        Tool("get_code_actions", "code-actions", "experimental", true, false, "List Roslyn code fixes and refactorings at a location."),
        Tool("preview_code_action", "code-actions", "experimental", true, false, "Preview a Roslyn code action before applying it."),
        Tool("apply_code_action", "code-actions", "experimental", false, true, "Apply a previously previewed Roslyn code action."),

        Tool("security_diagnostics", "security", "stable", true, false, "Return security-relevant diagnostics with OWASP categorization and fix hints."),
        Tool("security_analyzer_status", "security", "stable", true, false, "Check which security analyzer packages are present and recommend missing ones.")
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
        Prompt("debug_test_failure", "prompts", "experimental", true, false, "Generate a prompt for debugging a failing test."),
        Prompt("refactor_and_validate", "prompts", "experimental", true, false, "Generate a prompt for preview-first refactoring and validation."),
        Prompt("fix_all_diagnostics", "prompts", "experimental", true, false, "Generate a prompt for batched diagnostic cleanup."),
        Prompt("guided_package_migration", "prompts", "experimental", true, false, "Generate a prompt for package migration across affected projects."),
        Prompt("guided_extract_interface", "prompts", "experimental", true, false, "Generate a prompt for interface extraction and consumer updates."),
        Prompt("security_review", "prompts", "experimental", true, false, "Generate a prompt for comprehensive security review using security diagnostic tools."),
        Prompt("discover_capabilities", "prompts", "experimental", true, false, "Generate a prompt to discover relevant server tools and workflows for a task category."),
        Prompt("dead_code_audit", "prompts", "experimental", true, false, "Generate a prompt for dead code audit using unused symbol detection and removal."),
        Prompt("review_test_coverage", "prompts", "experimental", true, false, "Generate a prompt for test coverage review and gap identification."),
        Prompt("review_complexity", "prompts", "experimental", true, false, "Generate a prompt for complexity review and refactoring opportunities.")
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

    public static IReadOnlyList<WorkflowHint> WorkflowHints { get; } =
    [
        new("Preview/Apply", ["rename_preview", "rename_apply"], "Most write operations use a two-step pattern: call *_preview to inspect the diff, then *_apply with the preview token."),
        new("Diagnostic Fix", ["project_diagnostics", "diagnostic_details", "code_fix_preview", "code_fix_apply"], "Identify diagnostics, inspect details, preview a fix, then apply it."),
        new("Code Action", ["get_code_actions", "preview_code_action", "apply_code_action"], "List available Roslyn refactorings/fixes at a location, preview, then apply."),
        new("Security Audit", ["security_analyzer_status", "security_diagnostics", "diagnostic_details", "code_fix_preview", "code_fix_apply"], "Check analyzer coverage, get security findings, then fix them."),
        new("Build and Test", ["build_workspace", "test_run"], "Validate compilation then run tests after any code change."),
        new("Change Validation", ["test_related_files", "test_run"], "Find tests related to changed files, then run them."),
        new("Dead Code Cleanup", ["find_unused_symbols", "remove_dead_code_preview", "remove_dead_code_apply", "build_workspace"], "Find unused symbols, preview removal, apply, then verify build."),
        new("Package Migration", ["migrate_package_preview", "apply_composite_preview", "build_workspace"], "Preview migrating a package across projects, apply, then verify."),
        new("Coverage Analysis", ["test_discover", "test_coverage", "scaffold_test_preview", "scaffold_test_apply"], "Discover tests, run coverage, scaffold new tests for gaps.")
    ];

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

    private static int CountByTier(IReadOnlyList<SurfaceEntry> entries, string tier) =>
        entries.Count(entry => string.Equals(entry.SupportTier, tier, StringComparison.Ordinal));

    private static SurfaceEntry Tool(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("tool", name, category, supportTier, readOnly, destructive, summary, null);

    private static SurfaceEntry Resource(string name, string category, string supportTier, bool readOnly, bool destructive, string summary, string uriTemplate) =>
        new("resource", name, category, supportTier, readOnly, destructive, summary, uriTemplate);

    private static SurfaceEntry Prompt(string name, string category, string supportTier, bool readOnly, bool destructive, string summary) =>
        new("prompt", name, category, supportTier, readOnly, destructive, summary, null);
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
public sealed record SurfaceEntry(
    string Kind,
    string Name,
    string Category,
    string SupportTier,
    bool ReadOnly,
    bool Destructive,
    string Summary,
    string? UriTemplate);

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
/// Describes a common tool workflow — a sequence of tools that work together.
/// </summary>
/// <param name="Name">Human-readable workflow name (e.g., "Diagnostic Fix").</param>
/// <param name="ToolSequence">Ordered list of tool names in the typical execution flow.</param>
/// <param name="Description">Short description of when and how to use this workflow.</param>
public sealed record WorkflowHint(
    string Name,
    IReadOnlyList<string> ToolSequence,
    string Description);
