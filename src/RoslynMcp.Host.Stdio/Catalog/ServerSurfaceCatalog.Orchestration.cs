namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] OrchestrationTools =
    [
        Tool("get_prompt_text", "prompts", "experimental", true, false, "Render any registered MCP prompt as plain text. Pass the prompt name plus a JSON object of the prompt's parameters; returns { messages: [{role, text}], promptName, parameterCount }."),
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
        Tool("move_type_to_project_preview", "cross-project-refactoring", "experimental", true, false, "Preview moving a type declaration into another project."),
        Tool("extract_interface_cross_project_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface from a concrete type into a different project."),
        Tool("dependency_inversion_preview", "cross-project-refactoring", "experimental", true, false, "Preview extracting an interface and updating constructor dependencies."),
        Tool("migrate_package_preview", "orchestration", "experimental", true, false, "Preview migrating a package across affected projects."),
        Tool("split_class_preview", "orchestration", "experimental", true, false, "Preview splitting a class into a new partial file."),
        Tool("extract_and_wire_interface_preview", "orchestration", "experimental", true, false, "Preview extracting an interface and updating DI registrations."),
        Tool("apply_composite_preview", "orchestration", "experimental", false, true, "Apply a previously previewed orchestration operation."),
        Tool("get_syntax_tree", "syntax", "stable", true, false, "Return a structured syntax tree for a document or range."),
        Tool("security_diagnostics", "security", "stable", true, false, "Return security-relevant diagnostics with OWASP categorization and fix hints."),
        Tool("security_analyzer_status", "security", "stable", true, false, "Check which security analyzer packages are present and recommend missing ones."),
        Tool("nuget_vulnerability_scan", "security", "stable", true, false, "Scan NuGet references for known CVEs using dotnet list package --vulnerable."),
        Tool("evaluate_csharp", "scripting", "stable", true, false, "Evaluate a C# expression or script interactively via the Roslyn Scripting API. Emits MCP progress and heartbeat logs during long compile/run so clients are not stuck on a static label."),
        Tool("get_editorconfig_options", "configuration", "stable", true, false, "Get effective .editorconfig options for a source file."),
        Tool("set_editorconfig_option", "configuration", "stable", false, false, "Set or update a key in .editorconfig for C# files (creates file if needed)."),
        Tool("evaluate_msbuild_property", "project-mutation", "stable", true, false, "Evaluate a single MSBuild property for a project."),
        Tool("evaluate_msbuild_items", "project-mutation", "stable", true, false, "List MSBuild items of a type with evaluated includes and metadata."),
        Tool("get_msbuild_properties", "project-mutation", "stable", true, false, "Dump evaluated MSBuild properties for a project."),
        Tool("set_diagnostic_severity", "configuration", "stable", false, false, "Set dotnet_diagnostic severity in .editorconfig."),
    ];
}
