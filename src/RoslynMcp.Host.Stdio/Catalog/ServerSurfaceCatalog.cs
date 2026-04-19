namespace RoslynMcp.Host.Stdio.Catalog;

/// <summary>
/// Static inventory of all MCP tools, resources, and prompts exposed by the server,
/// together with their support tiers, categories, and read/destructive flags.
/// </summary>
public static class ServerSurfaceCatalog
{
    public const string CatalogVersion = "2026.04";

    public static IReadOnlyList<SurfaceEntry> Tools { get; } =
    [
        Tool("server_info", "server", "stable", true, false, "Inspect server capabilities, versions, and support tiers."),
        Tool("server_heartbeat", "server", "stable", true, false, "Lightweight connection readiness probe — returns state/loadedWorkspaceCount/stdioPid/serverStartedAt without the full server_info payload."),

        Tool("workspace_load", "workspace", "stable", false, false, "Load a .sln, .slnx, or .csproj into a named Roslyn workspace session."),
        Tool("workspace_reload", "workspace", "stable", false, false, "Reload an existing workspace session from disk."),
        Tool("workspace_close", "workspace", "stable", false, true, "Close a loaded workspace session and release resources."),
        Tool("workspace_list", "workspace", "stable", true, false, "List active workspace sessions."),
        Tool("workspace_status", "workspace", "stable", true, false, "Inspect status, diagnostics, and stale-state information for a workspace."),
        Tool("workspace_health", "workspace", "stable", true, false, "Lean workspace readiness summary (alias of workspace_status verbose=false)."),
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
        Tool("probe_position", "symbols", "experimental", true, false, "Probe the raw lexical token and containing symbol at a source position."),
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
        Tool("format_check", "refactoring", "experimental", true, false, "Report documents that would change under Roslyn's formatter without applying edits (workspace-wide format-verify)."),
        Tool("code_fix_preview", "refactoring", "stable", true, false, "Preview a curated diagnostic code fix."),
        Tool("code_fix_apply", "refactoring", "stable", false, true, "Apply a previously previewed curated code fix."),

        Tool("find_unused_symbols", "advanced-analysis", "stable", true, false, "Find likely unused symbols."),
        Tool("get_di_registrations", "advanced-analysis", "stable", true, false, "Inspect DI registration patterns in source."),
        Tool("get_complexity_metrics", "advanced-analysis", "stable", true, false, "Compute cyclomatic complexity and related metrics."),
        Tool("find_reflection_usages", "advanced-analysis", "stable", true, false, "Find reflection-heavy call sites."),
        Tool("trace_exception_flow", "advanced-analysis", "experimental", true, false, "Trace catch clauses that handle a given exception type."),
        Tool("get_namespace_dependencies", "advanced-analysis", "stable", true, false, "Build namespace dependency graphs."),
        Tool("get_nuget_dependencies", "advanced-analysis", "stable", true, false, "Inspect NuGet package references and versions."),
        Tool("semantic_search", "advanced-analysis", "stable", true, false, "Run semantic search over symbols and declarations."),
        Tool("find_duplicated_methods", "advanced-analysis", "stable", true, false, "Find clusters of near-duplicate method bodies by AST-normalized hash."),
        Tool("find_duplicate_helpers", "advanced-analysis", "experimental", true, false, "Flag private/internal helper methods whose body duplicates a reachable BCL/NuGet symbol."),
        Tool("find_dead_locals", "advanced-analysis", "experimental", true, false, "Find method-local variables whose only write is not followed by any read."),

        Tool("apply_text_edit", "editing", "stable", false, true, "Apply direct text edits to a single file; optional verify + auto-revert on new compile errors."),
        Tool("apply_multi_file_edit", "editing", "experimental", false, true, "Apply direct text edits to multiple files; optional verify + auto-revert on new compile errors."),
        Tool("preview_multi_file_edit", "editing", "experimental", true, false, "Preview a multi-file edit batch; returns per-file diffs and a preview token."),
        Tool("preview_multi_file_edit_apply", "editing", "experimental", false, true, "Apply a previously previewed multi-file edit."),
        Tool("restructure_preview", "refactoring", "experimental", true, false, "Preview a syntax-tree pattern-based find-and-replace using __placeholder__ captures."),
        Tool("replace_string_literals_preview", "refactoring", "experimental", true, false, "Preview replacing string literals in argument/initializer position with a constant expression."),
        Tool("symbol_impact_sweep", "analysis", "experimental", true, false, "Sweep downstream impact of a symbol change: references + switch-exhaustiveness diagnostics + mapper-suffix callsites."),
        Tool("test_reference_map", "validation", "experimental", true, false, "Statically map source symbols to test references; returns covered/uncovered symbol lists and a coverage percentage. Supports offset/limit pagination and projectName scoping."),
        Tool("get_prompt_text", "prompts", "experimental", true, false, "Render any registered MCP prompt as plain text. Pass the prompt name plus a JSON object of the prompt's parameters; returns { messages: [{role, text}], promptName, parameterCount }."),
        Tool("validate_workspace", "validation", "experimental", true, false, "Composite post-edit validation: compile_check + project_diagnostics (errors) + test_related_files (+ optional test_run)."),
        Tool("validate_recent_git_changes", "validation", "experimental", true, false, "post-edit-validate-workspace-scoped-to-touched-files: auto-scoped validation companion that derives changedFilePaths from git status --porcelain, falls back to full-workspace scope with a warning when git is unavailable or the solution is outside a git repo."),
        Tool("change_signature_preview", "refactoring", "experimental", true, false, "Preview adding/removing/renaming a method parameter with all callsites updated atomically."),
        Tool("symbol_refactor_preview", "refactoring", "experimental", true, false, "Composite preview chaining rename + edit + restructure operations into a single token."),
        Tool("split_service_with_di_preview", "refactoring", "experimental", true, false, "Composite preview splitting a service into partitions + forwarding facade + DI-registration deltas."),
        Tool("create_file_preview", "file-operations", "stable", true, false, "Preview creating a new source file in a project."),
        Tool("create_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file creation."),
        Tool("delete_file_preview", "file-operations", "stable", true, false, "Preview deleting an existing source file."),
        Tool("delete_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file deletion."),
        Tool("move_file_preview", "file-operations", "stable", true, false, "Preview moving a source file, optionally updating its namespace."),
        Tool("move_file_apply", "file-operations", "experimental", false, true, "Apply a previously previewed file move."),
        Tool("add_package_reference_preview", "project-mutation", "stable", true, false, "Preview adding a PackageReference to a project file."),
        Tool("remove_package_reference_preview", "project-mutation", "stable", true, false, "Preview removing a PackageReference from a project file."),
        Tool("add_project_reference_preview", "project-mutation", "stable", true, false, "Preview adding a ProjectReference to a project file."),
        Tool("remove_project_reference_preview", "project-mutation", "stable", true, false, "Preview removing a ProjectReference from a project file."),
        Tool("set_project_property_preview", "project-mutation", "stable", true, false, "Preview setting an allowlisted property in a project file."),
        Tool("add_target_framework_preview", "project-mutation", "stable", true, false, "Preview adding a target framework to a project file."),
        Tool("remove_target_framework_preview", "project-mutation", "stable", true, false, "Preview removing a target framework from a project file."),
        Tool("set_conditional_property_preview", "project-mutation", "stable", true, false, "Preview setting an allowlisted conditional project property."),
        Tool("add_central_package_version_preview", "project-mutation", "experimental", true, false, "Preview adding a PackageVersion entry to Directory.Packages.props."),
        Tool("remove_central_package_version_preview", "project-mutation", "stable", true, false, "Preview removing a PackageVersion entry from Directory.Packages.props."),
        Tool("apply_project_mutation", "project-mutation", "experimental", false, true, "Apply a previously previewed project file mutation."),
        Tool("scaffold_type_preview", "scaffolding", "experimental", true, false, "Preview scaffolding a new type file in a project."),
        Tool("scaffold_type_apply", "scaffolding", "experimental", false, true, "Apply a previously previewed type scaffolding operation."),
        Tool("scaffold_test_preview", "scaffolding", "stable", true, false, "Preview scaffolding a new test file (MSTest, xUnit, or NUnit; auto-detect or specify testFramework)."),
        Tool("scaffold_test_batch_preview", "scaffolding", "experimental", true, false, "Preview scaffolding multiple test files for related target types in one composite preview."),
        Tool("scaffold_first_test_file_preview", "scaffolding", "experimental", true, false, "Preview scaffolding the first <Service>Tests.cs fixture for a service that has no existing test file."),
        Tool("scaffold_test_apply", "scaffolding", "experimental", false, true, "Apply a previously previewed test scaffolding operation."),
        Tool("remove_dead_code_preview", "dead-code", "stable", true, false, "Preview removing unused symbols by handle."),
        Tool("remove_dead_code_apply", "dead-code", "experimental", false, true, "Apply a previously previewed dead-code removal operation."),
        Tool("move_type_to_project_preview", "cross-project-refactoring", "experimental", true, false, "Preview moving a type declaration into another project."),
        Tool("extract_interface_cross_project_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface from a concrete type into a different project."),
        Tool("dependency_inversion_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface and updating constructor dependencies."),
        Tool("migrate_package_preview", "orchestration", "experimental", true, false, "Preview migrating a package across affected projects."),
        Tool("split_class_preview", "orchestration", "experimental", true, false, "Preview splitting a class into a new partial file."),
        Tool("extract_and_wire_interface_preview", "orchestration", "experimental", true, false, "Preview extracting an interface and updating DI registrations."),
        Tool("apply_composite_preview", "orchestration", "experimental", false, true, "Apply a previously previewed orchestration operation."),
        Tool("test_coverage", "validation", "stable", false, false, "Run coverage collection for test execution."),
        Tool("get_syntax_tree", "syntax", "stable", true, false, "Return a structured syntax tree for a document or range."),
        Tool("get_code_actions", "code-actions", "stable", true, false, "List Roslyn code fixes and refactorings at a location or selection range. Selection-range refactorings include introduce parameter and inline temporary variable. Pass endLine/endColumn for selection-range actions."),
        Tool("preview_code_action", "code-actions", "stable", true, false, "Preview a Roslyn code action before applying it."),
        Tool("apply_code_action", "code-actions", "stable", false, true, "Apply a previously previewed Roslyn code action."),

        Tool("security_diagnostics", "security", "stable", true, false, "Return security-relevant diagnostics with OWASP categorization and fix hints."),
        Tool("security_analyzer_status", "security", "stable", true, false, "Check which security analyzer packages are present and recommend missing ones."),
        Tool("nuget_vulnerability_scan", "security", "stable", true, false, "Scan NuGet references for known CVEs using dotnet list package --vulnerable."),

        Tool("find_consumers", "analysis", "stable", true, false, "Find all types that depend on a given type or interface, classified by dependency kind."),
        Tool("get_cohesion_metrics", "analysis", "stable", true, false, "Measure type cohesion via LCOM4 metrics, identifying independent method clusters."),
        Tool("get_coupling_metrics", "analysis", "stable", true, false, "Measure per-type afferent/efferent coupling + Martin's instability index."),
        Tool("find_shared_members", "analysis", "stable", true, false, "Find private members used by multiple public methods to inform type extractions."),

        Tool("move_type_to_file_preview", "refactoring", "stable", true, false, "Preview moving a type declaration into its own file."),
        Tool("move_type_to_file_apply", "refactoring", "experimental", false, true, "Apply a previewed move-type-to-file refactoring. Removes the type from the source file and creates its own dedicated file."),
        Tool("change_type_namespace_preview", "refactoring", "experimental", true, false, "Preview relocating a type between namespaces in the same project. Rewrites the type's namespace declaration, optionally moves the file, and adjusts consumer using directives respecting ambient-namespace resolution."),
        Tool("extract_interface_preview", "refactoring", "experimental", true, false, "Preview extracting an interface from a concrete type within the same project. Optionally replaces concrete type references with the interface."),
        Tool("extract_interface_apply", "refactoring", "experimental", false, true, "Apply a previewed interface extraction. Creates the interface file, updates the type's base list, and applies usage replacements if requested."),
        Tool("bulk_replace_type_preview", "refactoring", "stable", true, false, "Preview replacing all references to one type with another across the solution. Scope can be 'parameters', 'fields', or 'all'. 'parameters' also covers generic arguments in implemented-interface / base-class declarations so the class's interface contract stays in sync. Useful after extracting an interface."),
        Tool("bulk_replace_type_apply", "refactoring", "experimental", false, true, "Apply a previewed bulk type replacement. Updates all matching type references and adds using directives where needed."),
        Tool("replace_invocation_preview", "refactoring", "experimental", true, false, "Preview rewriting every call-site of a method to call a different method with a declared argument-reorder mapping derived from parameter-name equality. Takes two FQ signatures and disambiguates overloads by parameter-type list."),
        Tool("extract_type_preview", "refactoring", "stable", true, false, "Preview extracting selected members from a type into a new type. Adds a private field and constructor parameter for composition. Use get_cohesion_metrics and find_shared_members to plan the extraction."),
        Tool("extract_type_apply", "refactoring", "experimental", false, true, "Apply a previewed type extraction. Moves members to the new type file and wires composition in the source type."),

        Tool("workspace_changes", "workspace", "stable", true, false, "List all mutations applied to a workspace during this session, with descriptions, affected files, tool names, and timestamps."),
        Tool("suggest_refactorings", "advanced-analysis", "stable", true, false, "Analyze the workspace and return ranked refactoring suggestions based on complexity, cohesion (LCOM4), and unused symbol detection. Each suggestion includes severity, target, and recommended tool sequence."),

        Tool("extract_method_preview", "refactoring", "stable", true, false, "Preview extracting selected statements into a new method. Uses data-flow analysis to infer parameters and return values."),
        Tool("extract_method_apply", "refactoring", "experimental", false, true, "Apply a previously previewed extract method refactoring."),
        Tool("extract_shared_expression_to_helper_preview", "refactoring", "experimental", true, false, "Preview extracting a shared sub-expression into a synthesized private static helper and rewriting every structurally-identical call site in the scope."),

        Tool("revert_last_apply", "undo", "stable", false, true, "Revert the most recent Roslyn solution-level apply operation for a workspace."),
        Tool("apply_with_verify", "undo", "experimental", false, true, "Apply a preview AND immediately verify via compile_check; auto-revert on new errors."),
        Tool("remove_interface_member_preview", "dead-code", "experimental", true, false, "Composite preview removing a dead interface member and every implementation in one shot. Refuses if any external caller exists."),

        Tool("analyze_data_flow", "advanced-analysis", "stable", true, false, "Analyze variable flow through a code region: reads, writes, captures, always-assigned."),
        Tool("analyze_control_flow", "advanced-analysis", "stable", true, false, "Analyze control flow: entry/exit points, reachability, return statements."),
        Tool("compile_check", "validation", "stable", true, false, "Fast in-memory compilation check without invoking dotnet build."),
        Tool("list_analyzers", "analysis", "stable", true, false, "List all loaded analyzers and their diagnostic rules."),
        Tool("fix_all_preview", "refactoring", "experimental", true, false, "Preview fixing ALL instances of a diagnostic across a scope."),
        Tool("fix_all_apply", "refactoring", "experimental", false, true, "Apply a previously previewed fix-all operation."),
        Tool("get_operations", "advanced-analysis", "stable", true, false, "Get the IOperation tree for behavioral analysis at a source position."),
        Tool("format_range_preview", "refactoring", "stable", true, false, "Preview formatting a specific range within a document."),
        Tool("format_range_apply", "refactoring", "experimental", false, true, "Apply a previously previewed range format operation."),
        Tool("analyze_snippet", "analysis", "stable", true, false, "Analyze a C# code snippet in an ephemeral workspace without loading a solution."),
        Tool("evaluate_csharp", "scripting", "stable", true, false, "Evaluate a C# expression or script interactively via the Roslyn Scripting API. Emits MCP progress and heartbeat logs during long compile/run so clients are not stuck on a static label."),
        Tool("get_editorconfig_options", "configuration", "stable", true, false, "Get effective .editorconfig options for a source file."),
        Tool("set_editorconfig_option", "configuration", "stable", false, false, "Set or update a key in .editorconfig for C# files (creates file if needed)."),
        Tool("evaluate_msbuild_property", "project-mutation", "stable", true, false, "Evaluate a single MSBuild property for a project."),
        Tool("evaluate_msbuild_items", "project-mutation", "stable", true, false, "List MSBuild items of a type with evaluated includes and metadata."),
        Tool("get_msbuild_properties", "project-mutation", "stable", true, false, "Dump evaluated MSBuild properties for a project."),
        Tool("set_diagnostic_severity", "configuration", "stable", false, false, "Set dotnet_diagnostic severity in .editorconfig."),
        Tool("add_pragma_suppression", "editing", "stable", false, false, "Insert a #pragma warning disable before a line."),
        Tool("verify_pragma_suppresses", "validation", "stable", true, false, "Verify an existing #pragma warning disable/restore pair covers a fire line."),
        Tool("pragma_scope_widen", "editing", "stable", false, false, "Extend an existing #pragma warning restore past a target line.")
    ];

    public static IReadOnlyList<SurfaceEntry> Resources { get; } =
    [
        Resource("server_catalog", "server", "stable", true, false, "Cap-safe summary of the server surface (tool/prompt counts + resource list + workflow hints). Full tools and prompts via paginated siblings.", "roslyn://server/catalog"),
        Resource("server_catalog_full", "server", "experimental", true, false, "Unpaginated full catalog including every tool and prompt entry. Large payload (~80 KB).", "roslyn://server/catalog/full"),
        Resource("server_catalog_tools_page", "server", "experimental", true, false, "Paginated slice of the server tool catalog. Slots: offset (0-based) + limit (1-200).", "roslyn://server/catalog/tools/{offset}/{limit}"),
        Resource("server_catalog_prompts_page", "server", "experimental", true, false, "Paginated slice of the server prompt catalog. Slots: offset (0-based) + limit (1-200).", "roslyn://server/catalog/prompts/{offset}/{limit}"),
        Resource("resource_templates", "server", "stable", true, false, "Lists all resource URI templates, including workspace-scoped templates.", "roslyn://server/resource-templates"),
        Resource("workspaces", "workspace", "stable", true, false, "List active workspace sessions (lean summary; counts and load state, no per-project tree).", "roslyn://workspaces"),
        Resource("workspaces_verbose", "workspace", "stable", true, false, "List active workspace sessions with full per-project tree and diagnostics.", "roslyn://workspaces/verbose"),
        Resource("workspace_status", "workspace", "stable", true, false, "Inspect workspace status (lean summary; counts and load state, no per-project tree).", "roslyn://workspace/{workspaceId}/status"),
        Resource("workspace_status_verbose", "workspace", "stable", true, false, "Inspect workspace status with full per-project tree and workspace diagnostics.", "roslyn://workspace/{workspaceId}/status/verbose"),
        Resource("workspace_projects", "workspace", "stable", true, false, "Read project graph metadata for a workspace.", "roslyn://workspace/{workspaceId}/projects"),
        Resource("workspace_diagnostics", "analysis", "stable", true, false, "Read all compiler diagnostics for a workspace.", "roslyn://workspace/{workspaceId}/diagnostics"),
        Resource("source_file", "workspace", "stable", true, false, "Read a source file from the loaded workspace.", "roslyn://workspace/{workspaceId}/file/{filePath}"),
        Resource("source_file_lines", "workspace", "experimental", true, false, "Read a 1-based inclusive line range from a source file in the loaded workspace.", "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}")
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
        Prompt("review_complexity", "prompts", "experimental", true, false, "Generate a prompt for complexity review and refactoring opportunities."),
        Prompt("cohesion_analysis", "prompts", "experimental", true, false, "Generate a prompt for SRP analysis using LCOM4 cohesion metrics with guided type extraction workflow."),
        Prompt("consumer_impact", "prompts", "experimental", true, false, "Generate a prompt analyzing the consumer/dependency graph for a type to assess refactoring impact."),
        Prompt("guided_extract_method", "prompts", "experimental", true, false, "Generate a prompt for extract-method refactoring with data-flow and control-flow checks."),
        Prompt("msbuild_inspection", "prompts", "experimental", true, false, "Generate a prompt for evaluating MSBuild properties and items for a project file."),
        Prompt("session_undo", "prompts", "experimental", true, false, "Generate a prompt for inspecting session mutations and undoing the last apply operation."),
        Prompt("refactor_loop", "prompts", "experimental", true, false, "Generate a prompt that walks an agent through the standard refactor → preview → apply-with-verify → validate_workspace loop using v1.17/v1.18 primitives.")
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
