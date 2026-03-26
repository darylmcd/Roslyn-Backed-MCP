using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Prompts;

[McpServerPromptType]
public static class RoslynPrompts
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        try
        {
            var details = await diagnosticService.GetDiagnosticDetailsAsync(workspaceId, diagnosticId, filePath, line, column, ct).ConfigureAwait(false);
            var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);

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

            var detailsJson = JsonSerializer.Serialize(details, JsonOptions);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("explain_error", ex)];
        }
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
        try
        {
            var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            if (sourceText is null)
                return [CreatePromptMessage($"File not found in workspace: {filePath}")];

            var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            var symbolsSummary = JsonSerializer.Serialize(symbols, JsonOptions);

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
                CreatePromptMessage($"""
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
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("suggest_refactoring", ex)];
        }
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
        try
        {
            var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            if (sourceText is null)
                return [CreatePromptMessage($"File not found in workspace: {filePath}")];

            var symbols = await symbolService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, null, ct).ConfigureAwait(false);

            var symbolsSummary = JsonSerializer.Serialize(symbols, JsonOptions);
            var diagnosticsSummary = JsonSerializer.Serialize(diagnostics, JsonOptions);

            return
            [
                CreatePromptMessage($"""
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
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("review_file", ex)];
        }
    }

    [McpServerPrompt(Name = "analyze_dependencies")]
    [Description("Generate a prompt to analyze architecture and dependency structure of a workspace")]
    public static async Task<IEnumerable<PromptMessage>> AnalyzeDependencies(
        IWorkspaceManager workspace,
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default)
    {
        try
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            var graphJson = JsonSerializer.Serialize(graph, JsonOptions);

            var namespaceDeps = await analysisService.GetNamespaceDependenciesAsync(workspaceId, null, ct).ConfigureAwait(false);
            var namespaceDepsJson = JsonSerializer.Serialize(namespaceDeps, JsonOptions);

            var nugetDeps = await analysisService.GetNuGetDependenciesAsync(workspaceId, ct).ConfigureAwait(false);
            var nugetDepsJson = JsonSerializer.Serialize(nugetDeps, JsonOptions);

            return
            [
                CreatePromptMessage($"""
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
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("analyze_dependencies", ex)];
        }
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
        try
        {
            var testResult = await validationService.RunTestsAsync(workspaceId, projectName, filter, ct).ConfigureAwait(false);
            var testResultJson = JsonSerializer.Serialize(testResult, JsonOptions);

            var failureSummary = testResult.Failures.Count > 0
                ? string.Join("\n", testResult.Failures.Select((f, i) =>
                    $"  {i + 1}. **{f.DisplayName}**\n     Message: {f.Message}\n     Stack: {f.StackTrace ?? "N/A"}"))
                : "No failures detected.";

            return
            [
                CreatePromptMessage($"""
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
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("debug_test_failure", ex)];
        }
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
        try
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("refactor_and_validate", ex)];
        }
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
        try
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("fix_all_diagnostics", ex)];
        }
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
        try
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("guided_package_migration", ex)];
        }
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
        try
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
                    {JsonSerializer.Serialize(documentSymbols, JsonOptions)}
                    ```

                    **Project Graph:**
                    ```json
                    {JsonSerializer.Serialize(projectGraph, JsonOptions)}
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("guided_extract_interface", ex)];
        }
    }

    // ── New prompts: Security and Agent Awareness ──

    [McpServerPrompt(Name = "security_review")]
    [Description("Generate a prompt that guides a comprehensive security review using security diagnostic tools and code fix workflows.")]
    public static async Task<IEnumerable<PromptMessage>> SecurityReview(
        ISecurityDiagnosticService securityService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var status = await securityService.GetAnalyzerStatusAsync(workspaceId, ct).ConfigureAwait(false);
            var findings = await securityService.GetSecurityDiagnosticsAsync(workspaceId, projectName, null, ct).ConfigureAwait(false);

            var statusSummary = new List<string>();
            statusSummary.Add($"- .NET SDK Analyzers: {(status.NetAnalyzersPresent ? "Present" : "Not detected")}");
            statusSummary.Add($"- SecurityCodeScan: {(status.SecurityCodeScanPresent ? "Present" : "Not installed")}");
            if (status.MissingRecommendedPackages.Count > 0)
            {
                statusSummary.Add($"- Recommended packages to add: {string.Join(", ", status.MissingRecommendedPackages)}");
            }

            var findingsSummary = findings.Findings.Take(20).Select(f =>
                $"- [{f.SecuritySeverity}] {f.DiagnosticId} ({f.OwaspCategory}): {f.Message} at {f.FilePath}:{f.StartLine}").ToArray();

            return
            [
                CreatePromptMessage($"""
                    Perform a comprehensive security review of this .NET workspace.

                    **Project Filter:** {projectName ?? "(entire workspace)"}

                    **Analyzer Coverage:**
                    {string.Join('\n', statusSummary)}

                    **Security Findings Summary:** {findings.TotalFindings} total ({findings.CriticalCount} critical, {findings.HighCount} high, {findings.MediumCount} medium, {findings.LowCount} low)

                    **Findings:**
                    {FormatBulletList(findingsSummary, "No security findings detected.")}

                    Use this workflow:
                    1. Review the analyzer coverage above. If recommended packages are missing, consider using `add_package_reference_preview` to add them, then `workspace_reload` to pick up new analyzers.
                    2. Triage findings by severity — address Critical and High findings first.
                    3. For each finding, call `diagnostic_details` with the diagnostic ID, file, line, and column to get detailed fix information.
                    4. If a Roslyn code fix is available, use `code_fix_preview` to inspect the proposed change, then `code_fix_apply` to apply it.
                    5. If no automated fix is available, use `get_code_actions` at the finding location, or apply manual fixes via `apply_text_edit` or `apply_multi_file_edit`.
                    6. After fixing a batch of findings, call `build_workspace` to verify the fixes compile.
                    7. Re-run `security_diagnostics` to confirm the finding count has decreased.
                    8. Flag any findings that require architectural changes or manual review rather than mechanical fixes.

                    Prioritize fixes that eliminate injection vulnerabilities and insecure deserialization patterns.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("security_review", ex)];
        }
    }

    [McpServerPrompt(Name = "discover_capabilities")]
    [Description("Generate a prompt that helps an agent discover relevant server tools and workflows for a given task category.")]
    public static Task<IEnumerable<PromptMessage>> DiscoverCapabilities(
        [Description("Task category: refactoring, analysis, security, testing, editing, navigation, project-mutation, scaffolding, or all")] string category)
    {
        try
        {
            var allTools = ServerSurfaceCatalog.Tools;
            var allPrompts = ServerSurfaceCatalog.Prompts;

            var normalizedCategory = category.Trim().ToLowerInvariant();
            var filteredTools = normalizedCategory == "all"
                ? allTools
                : allTools.Where(t => MatchesCategory(t.Category, normalizedCategory)).ToList();

            var toolList = filteredTools.Select(t =>
                $"- `{t.Name}` [{t.SupportTier}] {(t.Destructive ? "(destructive) " : "")}{(t.ReadOnly ? "(read-only) " : "")}— {t.Summary}").ToArray();

            var relevantPrompts = normalizedCategory == "all"
                ? allPrompts
                : allPrompts.Where(p => MatchesPromptCategory(p.Name, normalizedCategory)).ToList();

            var promptList = relevantPrompts.Select(p =>
                $"- `{p.Name}` — {p.Summary}").ToArray();

            var workflows = GetWorkflowsForCategory(normalizedCategory);

            IEnumerable<PromptMessage> result =
            [
                CreatePromptMessage($"""
                    Here are the server capabilities relevant to **{category}**:

                    **Tools ({filteredTools.Count}):**
                    {FormatBulletList(toolList, "No tools match this category.")}

                    **Guided Prompts ({relevantPrompts.Count}):**
                    {FormatBulletList(promptList, "No prompts match this category.")}

                    **Common Workflows:**
                    {workflows}

                    **Key Patterns:**
                    - **Preview/Apply**: Most write operations use a two-step preview-then-apply pattern. Call the `*_preview` tool first, inspect the diff, then call the corresponding `*_apply` tool with the preview token.
                    - **Validation**: After any code change, call `build_workspace` or `build_project` to verify compilation, then `test_run` or `test_related_files` if tests may be affected.
                    - **Workspace Gate**: All tools require a `workspaceId` from `workspace_load`. The workspace serializes concurrent requests.

                    Use the `server_catalog` resource at `roslyn://server/catalog` for the complete machine-readable inventory.
                    """)
            ];

            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<IEnumerable<PromptMessage>>([CreateErrorMessage("discover_capabilities", ex)]);
        }
    }

    [McpServerPrompt(Name = "dead_code_audit")]
    [Description("Generate a prompt that guides a dead code audit using unused symbol detection and removal tools.")]
    public static async Task<IEnumerable<PromptMessage>> DeadCodeAudit(
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var unused = await analysisService.FindUnusedSymbolsAsync(workspaceId, projectName, includePublic: false, limit: 50, ct).ConfigureAwait(false);
            var unusedSummary = unused.Take(20).Select(u =>
                $"- `{u.SymbolName}` ({u.SymbolKind}) in {u.FilePath}:{u.Line} — {u.ContainingType ?? "top-level"}").ToArray();

            return
            [
                CreatePromptMessage($"""
                    Perform a dead code audit for this workspace.

                    **Project Filter:** {projectName ?? "(entire workspace)"}
                    **Unused Symbols Found:** {unused.Count}

                    **Sample Unused Symbols (first 20):**
                    {FormatBulletList(unusedSummary, "No unused symbols detected.")}

                    Use this workflow:
                    1. Review the unused symbols above. Consider whether each is truly unused or accessed via reflection, serialization, or external entry points.
                    2. For symbols that are safe to remove, call `remove_dead_code_preview` with the symbol handles to preview the removal.
                    3. Inspect the preview diff — check that no other code is affected.
                    4. If the preview looks correct, call `remove_dead_code_apply` with the preview token.
                    5. After removal, call `build_workspace` to verify compilation succeeds.
                    6. Run `test_run` to confirm no tests break.
                    7. If more unused symbols remain, repeat the process with `find_unused_symbols`.

                    Be cautious with:
                    - Public API types that may be used by external consumers
                    - Types used via reflection (`find_reflection_usages` can help identify these)
                    - Event handlers and interface implementations that appear unused but are wired at runtime
                    - Serialization targets that are only instantiated during deserialization
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("dead_code_audit", ex)];
        }
    }

    [McpServerPrompt(Name = "review_test_coverage")]
    [Description("Generate a prompt that guides a test coverage review using test discovery, execution, and coverage tools.")]
    public static async Task<IEnumerable<PromptMessage>> ReviewTestCoverage(
        IValidationService validationService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var discovered = await validationService.DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
            var discoveredJson = JsonSerializer.Serialize(discovered, JsonOptions);

            return
            [
                CreatePromptMessage($"""
                    Review test coverage for this workspace and identify gaps.

                    **Project Filter:** {projectName ?? "(all test projects)"}

                    **Discovered Tests:**
                    ```json
                    {discoveredJson}
                    ```

                    Use this workflow:
                    1. Review the discovered tests above to understand current test coverage areas.
                    2. Call `test_coverage` to run tests with code coverage collection (requires coverlet.collector).
                    3. Analyze the coverage report to identify uncovered source files and methods.
                    4. For each uncovered area, use `document_symbols` and `symbol_info` to understand the code structure.
                    5. Use `scaffold_test_preview` to generate test stubs for uncovered types.
                    6. After scaffolding, call `scaffold_test_apply` to create the test files.
                    7. Use `test_run` to verify the new tests compile and run (they will initially fail or be skipped).
                    8. Prioritize coverage for:
                       - Public API surface (high-impact if broken)
                       - Complex methods (use `get_complexity_metrics` to identify high-complexity code)
                       - Security-sensitive code (use `security_diagnostics` to identify these areas)
                       - Recently changed code (high risk of regression)

                    Focus on meaningful coverage that validates behavior, not just line coverage.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("review_test_coverage", ex)];
        }
    }

    [McpServerPrompt(Name = "review_complexity")]
    [Description("Generate a prompt that guides a complexity review to identify and address high-complexity code.")]
    public static async Task<IEnumerable<PromptMessage>> ReviewComplexity(
        IAdvancedAnalysisService analysisService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var metrics = await analysisService.GetComplexityMetricsAsync(workspaceId, filePath: null, projectFilter: projectName, minComplexity: 5, limit: 50, ct).ConfigureAwait(false);
            var metricsJson = JsonSerializer.Serialize(metrics, JsonOptions);

            return
            [
                CreatePromptMessage($"""
                    Review code complexity for this workspace and identify refactoring opportunities.

                    **Project Filter:** {projectName ?? "(entire workspace)"}

                    **Complexity Metrics:**
                    ```json
                    {metricsJson}
                    ```

                    Use this workflow:
                    1. Identify methods with cyclomatic complexity > 10 — these are prime refactoring candidates.
                    2. Identify methods with nesting depth > 4 — consider guard clauses or early returns.
                    3. Identify methods with parameter count > 5 — consider parameter objects or builder patterns.
                    4. For each hotspot, call `get_code_actions` at the method declaration to see if Roslyn offers automated refactorings.
                    5. If automated refactorings are available, use `preview_code_action` and `apply_code_action`.
                    6. For manual refactoring:
                       - Use `callers_callees` to understand the method's call graph before changing its signature.
                       - Use `find_references` to identify all callers that would need updating.
                       - Use `impact_analysis` to estimate the blast radius of changes.
                    7. After refactoring, call `build_workspace` to verify compilation.
                    8. Call `test_related` or `test_related_files` to find and run affected tests.

                    Prioritize methods that are both high-complexity AND high-reference-count, as these have the most impact on maintainability.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [CreateErrorMessage("review_complexity", ex)];
        }
    }

    // ── Helpers ──

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

    private static PromptMessage CreateErrorMessage(string promptName, Exception ex)
    {
        return new PromptMessage
        {
            Role = Role.User,
            Content = new TextContentBlock
            {
                Text = $"The `{promptName}` prompt failed to gather context: {ex.Message}\n\nPlease verify the workspace is loaded and the parameters are correct, then try again."
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

    private static bool MatchesCategory(string toolCategory, string searchCategory) =>
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

    private static bool MatchesPromptCategory(string promptName, string searchCategory) =>
        searchCategory switch
        {
            "refactoring" => promptName is "suggest_refactoring" or "refactor_and_validate" or "guided_extract_interface",
            "analysis" => promptName is "analyze_dependencies" or "review_complexity",
            "security" => promptName is "security_review" or "review_file",
            "testing" => promptName is "debug_test_failure" or "review_test_coverage",
            "editing" => promptName is "fix_all_diagnostics",
            "navigation" => false,
            "project-mutation" => promptName is "guided_package_migration",
            "scaffolding" => promptName is "dead_code_audit",
            _ => true
        };

    private static string GetWorkflowsForCategory(string category) =>
        category switch
        {
            "refactoring" => """
                - **Code Action Flow**: `get_code_actions` → `preview_code_action` → `apply_code_action` → `build_workspace`
                - **Rename Flow**: `rename_preview` → `rename_apply` → `build_workspace` → `test_run`
                - **Curated Fix Flow**: `diagnostic_details` → `code_fix_preview` → `code_fix_apply` → `build_workspace`
                - **Extract Interface**: `extract_and_wire_interface_preview` → `apply_composite_preview` → `build_workspace`
                """,
            "analysis" => """
                - **Diagnostic Analysis**: `project_diagnostics` → `diagnostic_details` → `code_fix_preview`
                - **Architecture Review**: `project_graph` + `get_namespace_dependencies` + `get_nuget_dependencies`
                - **Complexity Review**: `get_complexity_metrics` → identify hotspots → `get_code_actions`
                - **Impact Assessment**: `impact_analysis` → `find_references` → `callers_callees`
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
