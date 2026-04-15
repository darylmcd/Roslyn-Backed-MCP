using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Prompts;

public static partial class RoslynPrompts
{
    [McpServerPrompt(Name = "debug_test_failure")]
    [Description("Generate a prompt to diagnose test failures by running tests and analyzing the output")]
    public static async Task<IEnumerable<PromptMessage>> DebugTestFailure(
        ITestRunnerService testRunnerService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        [Description("Optional: test filter expression to narrow which tests to run")] string? filter = null,
        CancellationToken ct = default)
    {
        try
        {
            var testResult = await testRunnerService.RunTestsAsync(workspaceId, projectName, filter, ct).ConfigureAwait(false);
            var testResultJson = JsonSerializer.Serialize(testResult, JsonDefaults.Indented);

            var failureSummary = testResult.Failures.Count > 0
                ? string.Join("\n", testResult.Failures.Select((f, i) =>
                    $"  {i + 1}. **{f.DisplayName}**\n     Message: {f.Message}\n     Stack: {f.StackTrace ?? "N/A"}"))
                : "No failures detected.";

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
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
            return [PromptMessageBuilder.CreateErrorMessage("debug_test_failure", ex)];
        }
    }

    [McpServerPrompt(Name = "refactor_and_validate")]
    [Description("Generate a prompt that composes code-action preview/apply with validation steps for a focused refactoring. Uses a 20 s soft cap and a Warning severity floor so a slow analyzer pass aborts cleanly with an actionable message instead of hanging through the framework's default timeout.")]
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
        // prompt-timeout-explain-refactor: cap at 20 s, pass severityFilter=Warning so PR #150's
        // analyzer-skip short-circuit fires when no Error-default analyzer is loaded.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(20));
        var promptCt = linkedCts.Token;

        try
        {
            var sourceText = await workspace.GetSourceTextAsync(workspaceId, filePath, promptCt).ConfigureAwait(false);
            if (sourceText is null)
            {
                return [PromptMessageBuilder.CreatePromptMessage($"File not found in workspace: {filePath}")];
            }

            var codeActions = await codeActionService.GetCodeActionsAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, promptCt).ConfigureAwait(false);
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, filePath, "Warning", null, promptCt).ConfigureAwait(false);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Plan and execute a focused refactoring for the selected C# code using the server's preview/apply workflow.

                    **File:** {filePath}
                    **Selection:** {startLine}:{startColumn}{PromptMessageBuilder.FormatRangeSuffix(endLine, endColumn)}

                    **Source Context:**
                    ```csharp
                    {PromptMessageBuilder.FormatSourceContext(sourceText, startLine, endLine)}
                    ```

                    **Available Code Actions:**
                    {PromptMessageBuilder.SummarizeCodeActions(codeActions)}

                    **Current Diagnostics In File:**
                    {PromptMessageBuilder.SummarizeDiagnostics(diagnostics)}

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
        catch (OperationCanceledException) when (promptCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return [PromptMessageBuilder.CreatePromptMessage(
                "refactor_and_validate: aborted because the analysis exceeded 20 s. Re-run on a narrower selection or warm the workspace first via project_diagnostics(severityFilter=\"Warning\").")];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [PromptMessageBuilder.CreateErrorMessage("refactor_and_validate", ex)];
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
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, projectName, null, severityFilter, null, ct).ConfigureAwait(false);
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
                PromptMessageBuilder.CreatePromptMessage($"""
                    Clean up diagnostics in this workspace using a preview-first loop.

                    **Project Filter:** {projectName ?? "(entire workspace)"}
                    **Severity Filter:** {severityFilter ?? "(all severities)"}

                    **Diagnostic Summary:**
                    {PromptMessageBuilder.FormatBulletList(groupedDiagnostics, "No compiler or analyzer diagnostics matched the filter.")}

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
            return [PromptMessageBuilder.CreateErrorMessage("fix_all_diagnostics", ex)];
        }
    }

    [McpServerPrompt(Name = "guided_package_migration")]
    [Description("Generate a prompt that walks a package migration across all affected projects.")]
    public static async Task<IEnumerable<PromptMessage>> GuidedPackageMigration(
        INuGetDependencyService nuGetDependencyService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Existing package id to replace")] string oldPackageId,
        [Description("Replacement package id")] string newPackageId,
        [Description("Replacement package version")] string newVersion,
        CancellationToken ct = default)
    {
        try
        {
            var dependencyResult = await nuGetDependencyService.GetNuGetDependenciesAsync(workspaceId, ct).ConfigureAwait(false);
            var matchingProjects = dependencyResult.Projects
                .Where(project => project.PackageReferences.Any(reference => string.Equals(reference.PackageId, oldPackageId, StringComparison.OrdinalIgnoreCase)))
                .Select(project => $"- {project.ProjectName}: {string.Join(", ", project.PackageReferences.Where(reference => string.Equals(reference.PackageId, oldPackageId, StringComparison.OrdinalIgnoreCase)).Select(reference => reference.Version))}")
                .ToArray();

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Migrate this solution from `{oldPackageId}` to `{newPackageId}` version `{newVersion}` using preview/apply tools.

                    **Projects Currently Using {oldPackageId}:**
                    {PromptMessageBuilder.FormatBulletList(matchingProjects, $"No projects currently reference {oldPackageId}.")}

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
            return [PromptMessageBuilder.CreateErrorMessage("guided_package_migration", ex)];
        }
    }

    [McpServerPrompt(Name = "guided_extract_interface")]
    [Description("Generate a prompt that guides interface extraction and consumer updates across a solution.")]
    public static async Task<IEnumerable<PromptMessage>> GuidedExtractInterface(
        IWorkspaceManager workspace,
        ISymbolSearchService symbolSearchService,
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
                return [PromptMessageBuilder.CreatePromptMessage($"File not found in workspace: {filePath}")];
            }

            var documentSymbols = await symbolSearchService.GetDocumentSymbolsAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            var projectGraph = workspace.GetProjectGraph(workspaceId);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Extract an interface from `{typeName}` and update downstream consumers using preview-first operations.

                    **File:** {filePath}
                    **Target Project:** {targetProjectName ?? "(same project unless the architecture suggests otherwise)"}

                    **Document Symbols:**
                    ```json
                    {JsonSerializer.Serialize(documentSymbols, JsonDefaults.Indented)}
                    ```

                    **Project Graph:**
                    ```json
                    {JsonSerializer.Serialize(projectGraph, JsonDefaults.Indented)}
                    ```

                    Use this workflow:
                    1. Locate the declaration span for `{typeName}` from the document-symbol output.
                            2. Prefer `extract_and_wire_interface_preview` if you want one orchestration preview that also updates DI registrations.
                            3. If you only need interface extraction, call `extract_interface_preview`, review the diff, then apply it with `extract_interface_apply`.
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
            return [PromptMessageBuilder.CreateErrorMessage("guided_extract_interface", ex)];
        }
    }

    [McpServerPrompt(Name = "refactor_loop")]
    [Description("Generate a prompt that walks an agent through the standard refactor → preview → apply-with-verify → validate_workspace loop using v1.17/v1.18 primitives. Mirrors the `refactor-loop` Claude Code skill so non-Claude clients have the same workflow.")]
    public static IEnumerable<PromptMessage> RefactorLoop(
        [Description("Natural-language refactor intent (e.g. 'rename GetUser to GetUserAsync and propagate to mappers').")] string intent,
        [Description("The workspace session identifier")] string workspaceId)
    {
        return
        [
            PromptMessageBuilder.CreatePromptMessage($"""
                Execute the following refactor against workspace `{workspaceId}` using the standard four-stage loop. Do not skip stages.

                **Intent:** {intent}

                ## Stage 1 — Pick the primitive
                Map the intent to ONE entry-point tool:
                - Single-symbol rename → `rename_preview`
                - Method extraction → `extract_method_preview`
                - Pattern rewrite → `restructure_preview` (with `__name__` placeholders)
                - Magic strings → `replace_string_literals_preview`
                - Multi-file ad-hoc edits → `preview_multi_file_edit`
                - Type organization → `move_type_to_file_preview` / `extract_type_preview` / `extract_interface_preview` / `split_class_preview`
                - Cross-project move → `move_type_to_project_preview`
                - Multi-step composite → `symbol_refactor_preview`

                If unsure, run `symbol_impact_sweep` first to surface references + switch-exhaustiveness diagnostics + mapper callsites.

                ## Stage 2 — Preview
                Call the chosen `*_preview` tool. Capture the `previewToken`. Show the per-file diff to the user. If `warnings: [...]` present, summarize and ask before proceeding.

                ## Stage 3 — Apply with verify
                Always prefer `apply_with_verify(previewToken)` — it runs the apply, then `compile_check`, and auto-reverts on new errors. Surface the resulting `status` (`applied` / `rolled_back` / `applied_with_errors`).

                ## Stage 4 — Validate
                Run `validate_workspace(workspaceId, runTests: true)`. Inspect `overallStatus`:
                - `clean` — done.
                - `compile-error` / `analyzer-error` — read `errorDiagnostics` and decide next action.
                - `test-failure` — read `testRunResult.failures` and decide next action.

                ## Stage 5 — Optional repeat
                If the intent implies more work, return to Stage 1 with the next sub-intent. Use `workspace_changes` to keep a session-wide ledger.

                ## Failure modes
                - Empty preview → primitive doesn't match; try `symbol_impact_sweep`.
                - `apply_with_verify` rolls back → read errors before retrying.
                - Stale workspace → v1.17 `ROSLYNMCP_ON_STALE=auto-reload` handles this transparently; check `_meta.staleAction`.

                Output a tight summary after each iteration: what changed, validation result, next suggestion.
                """)
        ];
    }
}

