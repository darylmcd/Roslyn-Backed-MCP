namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] ServerPrompts =
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

    public static IReadOnlyList<SurfaceEntry> Prompts => ServerPrompts;
}
