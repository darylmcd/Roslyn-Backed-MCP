using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class EditorConfigService : IEditorConfigService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<EditorConfigService> _logger;

    public EditorConfigService(IWorkspaceManager workspace, ILogger<EditorConfigService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<EditorConfigOptionsDto> GetOptionsAsync(
        string workspaceId, string filePath, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new FileNotFoundException($"Document not found in workspace: {filePath}");

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get syntax tree.");

        var options = new List<EditorConfigEntryDto>();

        // Get analyzer config options from the workspace's analyzer config documents
        var project = document.Project;
        var analyzerOptions = project.AnalyzerOptions;

        // Get options from the AnalyzerConfigOptionsProvider
        var optionsProvider = analyzerOptions.AnalyzerConfigOptionsProvider;
        var treeOptions = optionsProvider.GetOptions(tree);

        // Extract all known keys
        foreach (var key in GetKnownOptionKeys())
        {
            if (treeOptions.TryGetValue(key, out var value))
            {
                options.Add(new EditorConfigEntryDto(key, value, "editorconfig"));
            }
        }

        // Find the applicable .editorconfig path
        var editorconfigPath = FindEditorconfigPath(document.FilePath ?? filePath);

        return new EditorConfigOptionsDto(
            FilePath: filePath,
            ApplicableEditorConfigPath: editorconfigPath,
            Options: options.OrderBy(o => o.Key).ToList());
    }

    private static string? FindEditorconfigPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir is not null)
        {
            var editorconfigPath = Path.Combine(dir, ".editorconfig");
            if (File.Exists(editorconfigPath))
                return editorconfigPath;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static IEnumerable<string> GetKnownOptionKeys()
    {
        // Common .editorconfig keys that Roslyn respects
        return
        [
            // Core editor settings
            "indent_style",
            "indent_size",
            "tab_width",
            "end_of_line",
            "charset",
            "trim_trailing_whitespace",
            "insert_final_newline",
            "max_line_length",

            // .NET code style
            "dotnet_sort_system_directives_first",
            "dotnet_separate_import_directive_groups",
            "dotnet_style_qualification_for_field",
            "dotnet_style_qualification_for_property",
            "dotnet_style_qualification_for_method",
            "dotnet_style_qualification_for_event",
            "dotnet_style_predefined_type_for_locals_parameters_members",
            "dotnet_style_predefined_type_for_member_access",
            "dotnet_style_require_accessibility_modifiers",
            "dotnet_style_readonly_field",
            "dotnet_style_parentheses_in_arithmetic_binary_operators",
            "dotnet_style_parentheses_in_relational_binary_operators",
            "dotnet_style_parentheses_in_other_binary_operators",
            "dotnet_style_parentheses_in_other_operators",
            "dotnet_style_object_initializer",
            "dotnet_style_collection_initializer",
            "dotnet_style_prefer_auto_properties",
            "dotnet_style_explicit_tuple_names",
            "dotnet_style_prefer_inferred_tuple_names",
            "dotnet_style_prefer_inferred_anonymous_type_member_names",
            "dotnet_style_prefer_conditional_expression_over_assignment",
            "dotnet_style_prefer_conditional_expression_over_return",
            "dotnet_style_prefer_compound_assignment",
            "dotnet_style_prefer_simplified_interpolation",
            "dotnet_style_prefer_simplified_boolean_expressions",
            "dotnet_style_namespace_match_folder",
            "dotnet_style_coalesce_expression",
            "dotnet_style_null_propagation",
            "dotnet_style_prefer_is_null_check_over_reference_equality_method",

            // C# code style
            "csharp_style_var_for_built_in_types",
            "csharp_style_var_when_type_is_apparent",
            "csharp_style_var_elsewhere",
            "csharp_style_expression_bodied_methods",
            "csharp_style_expression_bodied_constructors",
            "csharp_style_expression_bodied_operators",
            "csharp_style_expression_bodied_properties",
            "csharp_style_expression_bodied_indexers",
            "csharp_style_expression_bodied_accessors",
            "csharp_style_expression_bodied_lambdas",
            "csharp_style_expression_bodied_local_functions",
            "csharp_style_pattern_matching_over_is_with_cast_check",
            "csharp_style_pattern_matching_over_as_with_null_check",
            "csharp_style_prefer_switch_expression",
            "csharp_style_prefer_pattern_matching",
            "csharp_style_prefer_not_pattern",
            "csharp_style_inlined_variable_declaration",
            "csharp_prefer_simple_default_expression",
            "csharp_style_prefer_local_over_anonymous_function",
            "csharp_style_deconstructed_variable_declaration",
            "csharp_style_prefer_index_operator",
            "csharp_style_prefer_range_operator",
            "csharp_style_implicit_object_creation_when_type_is_apparent",
            "csharp_style_prefer_tuple_swap",
            "csharp_style_unused_value_expression_statement_preference",
            "csharp_style_unused_value_assignment_preference",
            "csharp_prefer_static_local_function",
            "csharp_style_prefer_readonly_struct",
            "csharp_style_prefer_readonly_struct_member",
            "csharp_style_namespace_declarations",
            "csharp_style_prefer_null_check_over_type_check",
            "csharp_style_prefer_primary_constructors",
            "csharp_style_prefer_top_level_statements",
            "csharp_style_prefer_method_group_conversion",
            "csharp_using_directive_placement",

            // C# formatting
            "csharp_new_line_before_open_brace",
            "csharp_new_line_before_else",
            "csharp_new_line_before_catch",
            "csharp_new_line_before_finally",
            "csharp_new_line_before_members_in_object_initializers",
            "csharp_new_line_before_members_in_anonymous_types",
            "csharp_new_line_between_query_expression_clauses",
            "csharp_indent_case_contents",
            "csharp_indent_switch_labels",
            "csharp_indent_labels",
            "csharp_indent_block_contents",
            "csharp_indent_braces",
            "csharp_indent_case_contents_when_block",
            "csharp_space_after_cast",
            "csharp_space_after_keywords_in_control_flow_statements",
            "csharp_space_between_parentheses",
            "csharp_space_before_colon_in_inheritance_clause",
            "csharp_space_after_colon_in_inheritance_clause",
            "csharp_space_around_binary_operators",
            "csharp_space_between_method_declaration_parameter_list_parentheses",
            "csharp_space_between_method_declaration_empty_parameter_list_parentheses",
            "csharp_space_between_method_declaration_name_and_open_parenthesis",
            "csharp_space_between_method_call_parameter_list_parentheses",
            "csharp_space_between_method_call_empty_parameter_list_parentheses",
            "csharp_space_between_method_call_name_and_opening_parenthesis",
            "csharp_preserve_single_line_statements",
            "csharp_preserve_single_line_blocks",

            // Naming conventions
            "dotnet_naming_rule.interface_should_begin_with_i.severity",
            "dotnet_naming_rule.types_should_be_pascal_case.severity",
            "dotnet_naming_rule.non_field_members_should_be_pascal_case.severity",

            // Analyzer severity
            "dotnet_diagnostic.severity",
            "dotnet_analyzer_diagnostic.severity",

            // Nullable
            "dotnet_nullable",
        ];
    }

    public Task<EditorConfigWriteResultDto> SetOptionAsync(
        string workspaceId, string sourceFilePath, string key, string value, CancellationToken ct)
    {
        _ = _workspace.GetCurrentSolution(workspaceId);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        var normalizedSource = Path.GetFullPath(sourceFilePath);
        var editorconfigPath = FindEditorconfigPath(normalizedSource)
            ?? Path.Combine(Path.GetDirectoryName(normalizedSource) ?? throw new InvalidOperationException("Invalid source path."), ".editorconfig");

        var created = !File.Exists(editorconfigPath);
        var lines = created ? new List<string>() : File.ReadAllLines(editorconfigPath).ToList();

        const string csharpSection = "[*.{cs,csx,cake}]";
        UpsertKeyInSection(lines, csharpSection, key.Trim(), value.Trim());

        var directory = Path.GetDirectoryName(editorconfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(editorconfigPath, lines);

        return Task.FromResult(new EditorConfigWriteResultDto(editorconfigPath, key, value, created));
    }

    private static void UpsertKeyInSection(List<string> lines, string sectionHeader, string key, string value)
    {
        var assignment = $"{key} = {value}";
        var sectionIndex = lines.FindIndex(l => l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(sectionHeader);
            lines.Add(assignment);
            return;
        }

        var keyPrefix = key + " =";
        var end = lines.Count;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                end = i;
                break;
            }
        }

        for (var i = sectionIndex + 1; i < end; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = assignment;
                return;
            }
        }

        lines.Insert(end, assignment);
    }
}
