using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Prompts;

[McpServerPromptType]
public static class RoslynPrompts
{
    [McpServerPrompt(Name = "explain_error")]
    [Description("Generate a prompt to explain a compiler diagnostic error and suggest fixes")]
    public static async Task<IEnumerable<PromptMessage>> ExplainError(
        IDiagnosticService diagnosticService,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        var details = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, ct).ConfigureAwait(false);
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);

        // Extract surrounding lines for context
        var contextLines = "";
        if (sourceText is not null)
        {
            var lines = sourceText.Split('\n');
            var startLine = Math.Max(0, line - 6);
            var endLine = Math.Min(lines.Length - 1, line + 4);
            contextLines = string.Join('\n', lines[startLine..endLine].Select((l, i) =>
            {
                var lineNum = startLine + i + 1;
                var marker = lineNum == line ? " >>> " : "     ";
                return $"{marker}{lineNum,4}: {l.TrimEnd('\r')}";
            }));
        }

        var detailsJson = JsonSerializer.Serialize(details, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Explain the following C# compiler diagnostic and suggest how to fix it.

                        **Diagnostic:** {diagnosticId}
                        **File:** {filePath}
                        **Line:** {line}, **Column:** {column}

                        **Diagnostic Details:**
                        ```json
                        {detailsJson}
                        ```

                        **Source Context:**
                        ```csharp
                        {contextLines}
                        ```

                        Please:
                        1. Explain what this diagnostic means in plain language
                        2. Explain why it occurs in this context
                        3. Suggest one or more fixes with code examples
                        4. Note any potential side effects of each fix
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "suggest_refactoring")]
    [Description("Generate a prompt to analyze code and suggest refactorings")]
    public static async Task<IEnumerable<PromptMessage>> SuggestRefactoring(
        IWorkspaceManager workspace,
        ISymbolService symbolService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("Optional: start line to focus on")] int? startLine = null,
        [Description("Optional: end line to focus on")] int? endLine = null,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
            return [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = $"File not found in workspace: {filePath}" } }];

        var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        var symbolsSummary = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });

        string codeSection;
        if (startLine.HasValue && endLine.HasValue)
        {
            var lines = sourceText.Split('\n');
            var start = Math.Max(0, startLine.Value - 1);
            var end = Math.Min(lines.Length, endLine.Value);
            codeSection = string.Join('\n', lines[start..end].Select((l, i) => $"{start + i + 1,4}: {l.TrimEnd('\r')}"));
        }
        else
        {
            codeSection = sourceText;
        }

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Analyze the following C# code and suggest refactorings to improve its quality, readability, and maintainability.

                        **File:** {filePath}

                        **Document Symbols:**
                        ```json
                        {symbolsSummary}
                        ```

                        **Code:**
                        ```csharp
                        {codeSection}
                        ```

                        Please suggest refactorings considering:
                        1. SOLID principles violations
                        2. Code duplication opportunities
                        3. Method extraction candidates
                        4. Naming improvements
                        5. Pattern usage (e.g., Strategy, Factory, Builder)
                        6. Performance improvements
                        7. C# idiom improvements (pattern matching, LINQ, etc.)

                        For each suggestion, provide the specific code change and explain the benefit.
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "review_file")]
    [Description("Generate a prompt to perform a code review on a file")]
    public static async Task<IEnumerable<PromptMessage>> ReviewFile(
        IWorkspaceManager workspace,
        ISymbolService symbolService,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
            return [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = $"File not found in workspace: {filePath}" } }];

        var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, null, ct).ConfigureAwait(false);

        var symbolsSummary = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });
        var diagnosticsSummary = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Perform a thorough code review of the following C# source file.

                        **File:** {filePath}

                        **Document Symbols:**
                        ```json
                        {symbolsSummary}
                        ```

                        **Current Diagnostics:**
                        ```json
                        {diagnosticsSummary}
                        ```

                        **Source Code:**
                        ```csharp
                        {sourceText}
                        ```

                        Review the code for:
                        1. **Correctness**: Logic errors, edge cases, null handling
                        2. **Security**: Injection risks, input validation, sensitive data exposure
                        3. **Performance**: Unnecessary allocations, N+1 queries, missing async/await
                        4. **Thread Safety**: Race conditions, shared mutable state
                        5. **Design**: SOLID violations, coupling issues, missing abstractions
                        6. **Maintainability**: Readability, naming, documentation gaps
                        7. **Error Handling**: Missing try/catch, swallowed exceptions
                        8. **Testing**: Testability concerns, missing validation

                        For each issue found, specify the line number, severity (critical/major/minor/suggestion), and proposed fix.
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "analyze_dependencies")]
    [Description("Generate a prompt to analyze architecture and dependency structure of a workspace")]
    public static async Task<IEnumerable<PromptMessage>> AnalyzeDependencies(
        IWorkspaceManager workspace,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default)
    {
        var graph = workspace.GetProjectGraph(workspaceId);
        var graphJson = JsonSerializer.Serialize(graph, new JsonSerializerOptions { WriteIndented = true });

        var namespaceDeps = await analysisService.GetNamespaceDependenciesAsync(workspaceId, null, ct).ConfigureAwait(false);
        var namespaceDepsJson = JsonSerializer.Serialize(namespaceDeps, new JsonSerializerOptions { WriteIndented = true });

        var nugetDeps = await analysisService.GetNuGetDependenciesAsync(workspaceId, ct).ConfigureAwait(false);
        var nugetDepsJson = JsonSerializer.Serialize(nugetDeps, new JsonSerializerOptions { WriteIndented = true });

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Analyze the architecture and dependency structure of this .NET solution.

                        **Project Dependency Graph:**
                        ```json
                        {graphJson}
                        ```

                        **Namespace Dependencies:**
                        ```json
                        {namespaceDepsJson}
                        ```

                        **NuGet Dependencies:**
                        ```json
                        {nugetDepsJson}
                        ```

                        Please analyze:
                        1. **Architecture**: Identify the layering strategy and assess if it follows clean architecture / onion architecture principles
                        2. **Circular Dependencies**: Flag any circular namespace or project dependencies and suggest how to break them
                        3. **Coupling**: Identify tightly coupled components and suggest decoupling strategies
                        4. **NuGet Health**: Flag outdated, redundant, or conflicting package versions
                        5. **Dependency Direction**: Verify that dependencies flow in the correct direction (e.g., UI → Domain, not Domain → UI)
                        6. **Modularity**: Suggest opportunities to extract shared libraries or consolidate projects
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "debug_test_failure")]
    [Description("Generate a prompt to diagnose test failures by running tests and analyzing the output")]
    public static async Task<IEnumerable<PromptMessage>> DebugTestFailure(
        IValidationService validationService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        [Description("Optional: test filter expression to narrow which tests to run")] string? filter = null,
        CancellationToken ct = default)
    {
        var testResult = await validationService.RunTestsAsync(workspaceId, projectName, filter, ct).ConfigureAwait(false);
        var testResultJson = JsonSerializer.Serialize(testResult, new JsonSerializerOptions { WriteIndented = true });

        var failureSummary = testResult.Failures.Count > 0
            ? string.Join("\n", testResult.Failures.Select((f, i) =>
                $"  {i + 1}. **{f.DisplayName}**\n     Message: {f.Message}\n     Stack: {f.StackTrace ?? "N/A"}"))
            : "No failures detected.";

        return
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock
                {
                    Text = $"""
                        Diagnose the following test failure(s) and suggest fixes.

                        **Test Summary:** {testResult.Total} total, {testResult.Passed} passed, {testResult.Failed} failed, {testResult.Skipped} skipped

                        **Test Results (full):**
                        ```json
                        {testResultJson}
                        ```

                        **Failure Details:**
                        {failureSummary}

                        Please:
                        1. Identify the root cause of each failing test
                        2. Determine if the failure is in the test itself or the code under test
                        3. Suggest specific code changes to fix the failure
                        4. If the test expectation is wrong, explain why and suggest the correct assertion
                        5. Note any test isolation issues (shared state, timing, external dependencies)
                        """
                }
            }
        ];
    }

    [McpServerPrompt(Name = "refactor_and_validate")]
    [Description("Generate a prompt that composes code-action preview/apply with validation steps for a focused refactoring.")]
    public static async Task<IEnumerable<PromptMessage>> RefactorAndValidate(
        IWorkspaceManager workspace,
        ICodeActionService codeActionService,
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line number")] int startLine,
        [Description("1-based start column number")] int startColumn,
        [Description("Optional: 1-based end line number for a selection range")] int? endLine = null,
        [Description("Optional: 1-based end column number for a selection range")] int? endColumn = null,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
        {
            return [CreatePromptMessage($"File not found in workspace: {filePath}")];
        }

        var codeActions = await codeActionService.GetCodeActionsAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, ct).ConfigureAwait(false);
        var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, null, ct).ConfigureAwait(false);

        return
        [
            CreatePromptMessage($"""
                Plan and execute a focused refactoring for the selected C# code using the server's preview/apply workflow.

                **File:** {filePath}
                **Selection:** {startLine}:{startColumn}{FormatRangeSuffix(endLine, endColumn)}

                **Source Context:**
                ```csharp
                {FormatSourceContext(sourceText, startLine, endLine)}
                ```

                **Available Code Actions:**
                {SummarizeCodeActions(codeActions)}

                **Current Diagnostics In File:**
                {SummarizeDiagnostics(diagnostics)}

                Use this workflow:
                1. Call `get_code_actions` for the same span and select the best action index from the list above.
                2. Call `preview_code_action` with that action index and inspect the returned diff carefully.
                3. If the diff is acceptable, call `apply_code_action` with the preview token.
                4. If no appropriate code action exists, fall back to `rename_preview`, `organize_usings_preview`, `format_document_preview`, `apply_text_edit`, or `apply_multi_file_edit` as needed.
                5. After applying changes, call `build_project` for the owning project if obvious from the path; otherwise call `build_workspace`.
                6. If tests are likely impacted, call `test_related_files` or `test_run` and confirm whether any failures were introduced.
                7. Finish by calling `project_diagnostics` or `diagnostic_details` to confirm the targeted issue was reduced or resolved.

                Prefer minimal diffs and explain why you chose the selected code action over the alternatives.
                """)
        ];
    }

    [McpServerPrompt(Name = "fix_all_diagnostics")]
    [Description("Generate a prompt that batches diagnostic cleanup using code-fix and validation tools.")]
    public static async Task<IEnumerable<PromptMessage>> FixAllDiagnostics(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific project name")] string? projectName = null,
        [Description("Optional: severity filter such as error, warning, or info")] string? severityFilter = null,
        CancellationToken ct = default)
    {
        var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, projectName, null, severityFilter, ct).ConfigureAwait(false);
        var groupedDiagnostics = diagnostics.CompilerDiagnostics
            .Concat(diagnostics.AnalyzerDiagnostics)
            .GroupBy(diagnostic => diagnostic.Id, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(group => $"- {group.Key}: {group.Count()} occurrence(s)")
            .ToArray();

        return
        [
            CreatePromptMessage($"""
                Clean up diagnostics in this workspace using a preview-first loop.

                **Project Filter:** {projectName ?? "(entire workspace)"}
                **Severity Filter:** {severityFilter ?? "(all severities)"}

                **Diagnostic Summary:**
                {FormatBulletList(groupedDiagnostics, "No compiler or analyzer diagnostics matched the filter.")}

                Use this workflow:
                1. Start with the highest-volume or highest-severity diagnostic group.
                2. For one representative occurrence, call `diagnostic_details` to inspect curated fix metadata.
                3. If a curated fix exists, call `code_fix_preview`, review the diff, then `code_fix_apply`.
                4. If no curated fix exists, use `get_code_actions` and `preview_code_action` at the relevant span, or fall back to text-edit tools.
                5. After each applied batch, call `build_project` or `build_workspace` and verify the count for that diagnostic ID decreases.
                6. Repeat until the remaining diagnostics require manual design changes rather than mechanical fixes.

                Keep changes batched by diagnostic ID so validation remains attributable.
                """)
        ];
    }

    [McpServerPrompt(Name = "guided_package_migration")]
    [Description("Generate a prompt that walks a package migration across all affected projects.")]
    public static async Task<IEnumerable<PromptMessage>> GuidedPackageMigration(
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Existing package id to replace")] string oldPackageId,
        [Description("Replacement package id")] string newPackageId,
        [Description("Replacement package version")] string newVersion,
        CancellationToken ct = default)
    {
        var dependencyResult = await analysisService.GetNuGetDependenciesAsync(workspaceId, ct).ConfigureAwait(false);
        var matchingProjects = dependencyResult.Projects
            .Where(project => project.PackageReferences.Any(reference => string.Equals(reference.PackageId, oldPackageId, StringComparison.OrdinalIgnoreCase)))
            .Select(project => $"- {project.ProjectName}: {string.Join(", ", project.PackageReferences.Where(reference => string.Equals(reference.PackageId, oldPackageId, StringComparison.OrdinalIgnoreCase)).Select(reference => reference.Version))}")
            .ToArray();

        return
        [
            CreatePromptMessage($"""
                Migrate this solution from `{oldPackageId}` to `{newPackageId}` version `{newVersion}` using preview/apply tools.

                **Projects Currently Using {oldPackageId}:**
                {FormatBulletList(matchingProjects, $"No projects currently reference {oldPackageId}.")}

                Use this workflow:
                        1. Prefer a single call to `migrate_package_preview` and inspect the combined diff across all affected project files.
                        2. If the preview looks correct, call `apply_composite_preview`.
                        3. If you need more granular control, fall back to `remove_package_reference_preview`, `add_package_reference_preview`, and `add_central_package_version_preview` per project.
                        4. After project-file changes, call `build_workspace` and inspect resulting diagnostics.
                        5. For any API or namespace incompatibilities, use `symbol_search`, `find_references`, `get_code_actions`, `preview_code_action`, or text-edit tools to adapt consumers.
                        6. Finish with `test_run` or `test_related_files` for impacted areas.

                Prefer migrating all project references first, then fixing source-level API fallout in a second pass.
                """)
        ];
    }

    [McpServerPrompt(Name = "guided_extract_interface")]
    [Description("Generate a prompt that guides interface extraction and consumer updates across a solution.")]
    public static async Task<IEnumerable<PromptMessage>> GuidedExtractInterface(
        IWorkspaceManager workspace,
        ISymbolService symbolService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file containing the target type")] string filePath,
        [Description("Name of the concrete type to extract an interface from")] string typeName,
        [Description("Optional: target project for the extracted interface")] string? targetProjectName = null,
        CancellationToken ct = default)
    {
        var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        if (sourceText is null)
        {
            return [CreatePromptMessage($"File not found in workspace: {filePath}")];
        }

        var documentSymbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        var projectGraph = workspace.GetProjectGraph(workspaceId);

        return
        [
            CreatePromptMessage($"""
                Extract an interface from `{typeName}` and update downstream consumers using preview-first operations.

                **File:** {filePath}
                **Target Project:** {targetProjectName ?? "(same project unless the architecture suggests otherwise)"}

                **Document Symbols:**
                ```json
                {JsonSerializer.Serialize(documentSymbols, new JsonSerializerOptions { WriteIndented = true })}
                ```

                **Project Graph:**
                ```json
                {JsonSerializer.Serialize(projectGraph, new JsonSerializerOptions { WriteIndented = true })}
                ```

                Use this workflow:
                1. Locate the declaration span for `{typeName}` from the document-symbol output.
                        2. Prefer `extract_and_wire_interface_preview` if you want one orchestration preview that also updates DI registrations.
                        3. If you only need interface extraction, call `extract_interface_preview`, review the diff, then apply it with `rename_apply`.
                        4. If no suitable dedicated flow exists, fall back to `get_code_actions`, `preview_code_action`, or manual scaffolding with `scaffold_type_preview`.
                        5. If the interface should live in another project, ensure the necessary project reference exists in the preview before applying.
                        6. Call `find_references` for `{typeName}` and update consumer type annotations or constructor parameters where interface-based dependencies are more appropriate.
                        7. Finish with `build_workspace`, then `test_related` or `test_run` for the affected components.

                Prefer preserving the concrete type while introducing the interface incrementally unless a broader dependency inversion is explicitly required.
                """)
        ];
    }

    private static PromptMessage CreatePromptMessage(string text)
    {
        return new PromptMessage
        {
            Role = Role.User,
            Content = new TextContentBlock
            {
                Text = text
            }
        };
    }

    private static string FormatSourceContext(string sourceText, int startLine, int? endLine)
    {
        var lines = sourceText.Split('\n');
        var start = Math.Max(0, startLine - 4);
        var endExclusive = Math.Min(lines.Length, Math.Max(endLine ?? startLine, startLine) + 3);
        return string.Join('\n', lines[start..endExclusive].Select((line, index) =>
            $"{start + index + 1,4}: {line.TrimEnd('\r')}"));
    }

    private static string FormatRangeSuffix(int? endLine, int? endColumn)
    {
        return endLine.HasValue && endColumn.HasValue
            ? $" - {endLine.Value}:{endColumn.Value}"
            : string.Empty;
    }

    private static string SummarizeCodeActions(IReadOnlyList<RoslynMcp.Core.Models.CodeActionDto> codeActions)
    {
        if (codeActions.Count == 0)
        {
            return "No Roslyn code actions are currently available at this span.";
        }

        return string.Join('\n', codeActions.Take(12).Select(action =>
            $"- [{action.Index}] {action.Title} ({action.Kind})"));
    }

    private static string SummarizeDiagnostics(RoslynMcp.Core.Models.DiagnosticsResultDto diagnostics)
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

    private static string FormatBulletList(IReadOnlyList<string> lines, string emptyMessage)
    {
        return lines.Count == 0 ? emptyMessage : string.Join('\n', lines);
    }
}
