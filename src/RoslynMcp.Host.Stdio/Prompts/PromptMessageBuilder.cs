using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Prompts;

/// <summary>
/// Shared formatting and catalog routing helpers for MCP prompt types.
/// </summary>
internal static class PromptMessageBuilder
{
    internal static string TruncateSourceLines(string source, int maxLines)
    {
        var lines = source.Split('\n');
        if (lines.Length <= maxLines) return source;
        return string.Join('\n', lines.Take(maxLines)) + $"\n// ... [{lines.Length - maxLines} more lines truncated]";
    }

    internal static string SerializeTruncatedList<T>(IReadOnlyList<T> items, int max, JsonSerializerOptions options)
    {
        if (items.Count <= max)
            return JsonSerializer.Serialize(items, options);

        var json = JsonSerializer.Serialize(items.Take(max).ToList(), options);
        return json + $"\n[Showing {max} of {items.Count} items]";
    }

    internal static PromptMessage CreatePromptMessage(string text) =>
        new()
        {
            Role = Role.User,
            Content = new TextContentBlock { Text = text }
        };

    internal static PromptMessage CreateErrorMessage(string promptName, Exception ex) =>
        new()
        {
            Role = Role.User,
            Content = new TextContentBlock
            {
                Text = $"The `{promptName}` prompt failed to gather context: {ex.Message}\n\nPlease verify the workspace is loaded and the parameters are correct, then try again."
            }
        };

    internal static string FormatSourceContext(string sourceText, int startLine, int? endLine)
    {
        var lines = sourceText.Split('\n');
        var start = Math.Max(0, startLine - 4);
        var endExclusive = Math.Min(lines.Length, Math.Max(endLine ?? startLine, startLine) + 3);
        return string.Join('\n', lines[start..endExclusive].Select((line, index) =>
            $"{start + index + 1,4}: {line.TrimEnd('\r')}"));
    }

    internal static string FormatRangeSuffix(int? endLine, int? endColumn) =>
        endLine.HasValue && endColumn.HasValue
            ? $" - {endLine.Value}:{endColumn.Value}"
            : string.Empty;

    internal static string SummarizeCodeActions(IReadOnlyList<CodeActionDto> codeActions)
    {
        if (codeActions.Count == 0)
            return "No Roslyn code actions are currently available at this span.";

        return string.Join('\n', codeActions.Take(12).Select(action =>
            $"- [{action.Index}] {action.Title} ({action.Kind})"));
    }

    internal static string SummarizeDiagnostics(DiagnosticsResultDto diagnostics)
    {
        var relevantDiagnostics = diagnostics.CompilerDiagnostics
            .Concat(diagnostics.AnalyzerDiagnostics)
            .Take(10)
            .Select(diagnostic => $"- {diagnostic.Id} {diagnostic.Severity} at {diagnostic.FilePath}:{diagnostic.StartLine}:{diagnostic.StartColumn} - {diagnostic.Message}")
            .ToArray();

        return FormatBulletList(
            relevantDiagnostics,
            $"No matching diagnostics. Totals: {diagnostics.TotalErrors} errors, {diagnostics.TotalWarnings} warnings, {diagnostics.TotalInfo} info.");
    }

    internal static string FormatBulletList(IReadOnlyList<string> lines, string emptyMessage) =>
        lines.Count == 0 ? emptyMessage : string.Join('\n', lines);

    internal static bool MatchesCategory(string toolCategory, string searchCategory) =>
        searchCategory switch
        {
            "refactoring" => toolCategory is "refactoring" or "code-actions" or "cross-project-refactoring" or "orchestration",
            "analysis" => toolCategory is "analysis" or "advanced-analysis",
            "security" => toolCategory is "security",
            "testing" => toolCategory is "validation",
            "editing" => toolCategory is "editing" or "file-operations",
            "navigation" => toolCategory is "symbols",
            "project-mutation" => toolCategory is "project-mutation",
            "scaffolding" => toolCategory is "scaffolding" or "dead-code",
            _ => true
        };

    internal static bool MatchesPromptCategory(string promptName, string searchCategory) =>
        searchCategory switch
        {
            "refactoring" => promptName is "suggest_refactoring" or "refactor_and_validate" or "guided_extract_interface" or "guided_extract_method" or "cohesion_analysis" or "consumer_impact",
            "analysis" => promptName is "analyze_dependencies" or "review_complexity" or "cohesion_analysis" or "consumer_impact",
            "security" => promptName is "security_review" or "review_file",
            "testing" => promptName is "debug_test_failure" or "review_test_coverage",
            "editing" => promptName is "fix_all_diagnostics" or "session_undo",
            "navigation" => false,
            "project-mutation" => promptName is "guided_package_migration" or "msbuild_inspection",
            "scaffolding" => promptName is "dead_code_audit",
            _ => true
        };

    internal static string GetWorkflowsForCategory(string category) =>
        category switch
        {
            "refactoring" => """
                - **Code Action Flow**: `get_code_actions` → `preview_code_action` → `apply_code_action` → `build_workspace`
                - **Rename Flow**: `rename_preview` → `rename_apply` → `build_workspace` → `test_run`
                - **Curated Fix Flow**: `diagnostic_details` → `code_fix_preview` → `code_fix_apply` → `build_workspace`
                - **Extract Interface**: `extract_interface_preview` → `extract_interface_apply` → `bulk_replace_type_preview` → `bulk_replace_type_apply`
                - **Extract Type (SRP)**: `get_cohesion_metrics` → `find_shared_members` → `extract_type_preview` → `extract_type_apply`
                - **Move Type to File**: `move_type_to_file_preview` → `move_type_to_file_apply`
                """,
            "analysis" => """
                - **Diagnostic Analysis**: `project_diagnostics` → `diagnostic_details` → `code_fix_preview`
                - **Architecture Review**: `project_graph` + `get_namespace_dependencies` + `get_nuget_dependencies`
                - **Complexity Review**: `get_complexity_metrics` → identify hotspots → `get_code_actions`
                - **Impact Assessment**: `impact_analysis` → `find_references` → `callers_callees`
                - **SRP / Cohesion Analysis**: `get_cohesion_metrics` → `find_shared_members` → `extract_type_preview`
                - **Consumer Analysis**: `find_consumers` → understand dependency kinds → plan interface extraction
                """,
            "security" => """
                - **Security Audit**: `security_analyzer_status` → `security_diagnostics` → `diagnostic_details` → `code_fix_preview` → `code_fix_apply`
                - **Security Coverage**: `security_analyzer_status` → `add_package_reference_preview` (add SecurityCodeScan) → `workspace_reload`
                """,
            "testing" => """
                - **Test Discovery**: `test_discover` → `test_run` → `debug_test_failure` (if failures)
                - **Coverage Analysis**: `test_coverage` → identify gaps → `scaffold_test_preview` → `scaffold_test_apply`
                - **Change Validation**: `test_related_files` → `test_run` (filtered)
                """,
            "editing" => """
                - **Single File Edit**: `apply_text_edit` → `build_project`
                - **Multi-File Edit**: `apply_multi_file_edit` → `build_workspace`
                - **File Operations**: `create_file_preview` → `create_file_apply`, `move_file_preview` → `move_file_apply`
                """,
            "navigation" => """
                - **Symbol Exploration**: `symbol_search` → `symbol_info` → `go_to_definition`
                - **Reference Tracing**: `find_references` → `find_implementations` → `callers_callees`
                - **Hierarchy Navigation**: `type_hierarchy` → `find_overrides` → `find_base_members`
                - **Context Discovery**: `enclosing_symbol` → `symbol_relationships`
                """,
            "project-mutation" => """
                - **Add Package**: `add_package_reference_preview` → `apply_project_mutation` → `workspace_reload` → `build_workspace`
                - **Package Migration**: `migrate_package_preview` → `apply_composite_preview` → `build_workspace`
                - **Central Versioning**: `add_central_package_version_preview` → `apply_project_mutation`
                """,
            "scaffolding" => """
                - **New Type**: `scaffold_type_preview` → `scaffold_type_apply` → `build_project`
                - **New Test**: `scaffold_test_preview` → `scaffold_test_apply` → `test_run`
                - **Dead Code Cleanup**: `find_unused_symbols` → `remove_dead_code_preview` → `remove_dead_code_apply` → `build_workspace`
                """,
            _ => """
                - **Preview/Apply Pattern**: Most write tools follow `*_preview` → inspect diff → `*_apply` → validate
                - **Validation**: `build_workspace` or `build_project` after any code change, then `test_run` if tests may be affected
                - **Diagnostics Flow**: `project_diagnostics` → `diagnostic_details` → `code_fix_preview` → `code_fix_apply`
                - **Security Flow**: `security_analyzer_status` → `security_diagnostics` → triage → fix
                """
        };
}
