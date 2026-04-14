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
    // ── New prompts: Security and Agent Awareness ──

    [McpServerPrompt(Name = "security_review")]
    [Description("Generate a prompt that guides a comprehensive security review using security diagnostic tools and code fix workflows.")]
    public static async Task<IEnumerable<PromptMessage>> SecurityReview(
        ISecurityDiagnosticService securityService,
        INuGetDependencyService nuGetDependencyService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var status = await securityService.GetAnalyzerStatusAsync(workspaceId, ct).ConfigureAwait(false);
            var findings = await securityService.GetSecurityDiagnosticsAsync(workspaceId, projectName, null, ct).ConfigureAwait(false);
            NuGetVulnerabilityScanResultDto? vulnScan = null;
            try
            {
                vulnScan = await nuGetDependencyService.ScanNuGetVulnerabilitiesAsync(
                    workspaceId, projectName, includeTransitive: false, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Network/SDK: still emit prompt; agent can run nuget_vulnerability_scan manually.
            }

            var statusSummary = new List<string>();
            statusSummary.Add($"- .NET SDK Analyzers: {(status.NetAnalyzersPresent ? "Present" : "Not detected")}");
            statusSummary.Add($"- SecurityCodeScan: {(status.SecurityCodeScanPresent ? "Present" : "Not installed")}");
            if (status.MissingRecommendedPackages.Count > 0)
            {
                statusSummary.Add($"- Recommended packages to add: {string.Join(", ", status.MissingRecommendedPackages)}");
            }

            var findingsSummary = findings.Findings.Take(20).Select(f =>
                $"- [{f.SecuritySeverity}] {f.DiagnosticId} ({f.OwaspCategory}): {f.Message} at {f.FilePath}:{f.StartLine}").ToArray();

            var vulnSummary = vulnScan is null
                ? "(NuGet vulnerability scan was not run in this prompt — call `nuget_vulnerability_scan` on the workspace.)"
                : $"{vulnScan.TotalVulnerabilities} vulnerable package reference(s) ({vulnScan.CriticalCount} critical, {vulnScan.HighCount} high, {vulnScan.MediumCount} medium, {vulnScan.LowCount} low). Scanned projects: {vulnScan.ScannedProjects}.";

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Perform a comprehensive security review of this .NET workspace.

                    **Project Filter:** {projectName ?? "(entire workspace)"}

                    **Analyzer Coverage:**
                    {string.Join('\n', statusSummary)}

                    **NuGet dependency vulnerabilities (CVE database):**
                    {vulnSummary}

                    **Security Findings Summary:** {findings.TotalFindings} total ({findings.CriticalCount} critical, {findings.HighCount} high, {findings.MediumCount} medium, {findings.LowCount} low)

                    **Findings:**
                    {PromptMessageBuilder.FormatBulletList(findingsSummary, "No security findings detected.")}

                    Use this workflow:
                    1. Review the analyzer coverage above. If recommended packages are missing, consider using `add_package_reference_preview` to add them, then `workspace_reload` to pick up new analyzers.
                    2. Run `nuget_vulnerability_scan` (optionally with `includeTransitive: true`) to refresh dependency CVE data. Treat Critical/High package vulnerabilities as urgent; use `add_package_reference_preview` / package upgrades to resolved patched versions when available.
                    3. Triage Roslyn security findings by severity — address Critical and High first.
                    4. For each finding, call `diagnostic_details` with the diagnostic ID, file, line, and column to get detailed fix information.
                    5. If a Roslyn code fix is available, use `code_fix_preview` to inspect the proposed change, then `code_fix_apply` to apply it.
                    6. If no automated fix is available, use `get_code_actions` at the finding location, or apply manual fixes via `apply_text_edit` or `apply_multi_file_edit`.
                    7. After fixing a batch of findings, call `build_workspace` to verify the fixes compile.
                    8. Re-run `security_diagnostics` and `nuget_vulnerability_scan` to confirm exposure has decreased.
                    9. Flag any findings that require architectural changes or manual review rather than mechanical fixes.

                    Prioritize fixes that eliminate injection vulnerabilities and insecure deserialization patterns.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [PromptMessageBuilder.CreateErrorMessage("security_review", ex)];
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
                : allTools.Where(t => PromptMessageBuilder.MatchesCategory(t.Category, normalizedCategory)).ToList();

            var toolList = filteredTools.Select(t =>
                $"- `{t.Name}` [{t.SupportTier}] {(t.Destructive ? "(destructive) " : "")}{(t.ReadOnly ? "(read-only) " : "")}— {t.Summary}").ToArray();

            var relevantPrompts = normalizedCategory == "all"
                ? allPrompts
                : allPrompts.Where(p => PromptMessageBuilder.MatchesPromptCategory(p.Name, normalizedCategory)).ToList();

            var promptList = relevantPrompts.Select(p =>
                $"- `{p.Name}` — {p.Summary}").ToArray();

            var workflows = PromptMessageBuilder.GetWorkflowsForCategory(normalizedCategory);

            IEnumerable<PromptMessage> result =
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Here are the server capabilities relevant to **{category}**:

                    **Tools ({filteredTools.Count}):**
                    {PromptMessageBuilder.FormatBulletList(toolList, "No tools match this category.")}

                    **Guided Prompts ({relevantPrompts.Count}):**
                    {PromptMessageBuilder.FormatBulletList(promptList, "No prompts match this category.")}

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
            return Task.FromResult<IEnumerable<PromptMessage>>([PromptMessageBuilder.CreateErrorMessage("discover_capabilities", ex)]);
        }
    }

    [McpServerPrompt(Name = "dead_code_audit")]
    [Description("Generate a prompt that guides a dead code audit using unused symbol detection and removal tools.")]
    public static async Task<IEnumerable<PromptMessage>> DeadCodeAudit(
        IUnusedCodeAnalyzer unusedCodeAnalyzer,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var unused = await unusedCodeAnalyzer.FindUnusedSymbolsAsync(
                workspaceId,
                new UnusedSymbolsAnalysisOptions
                {
                    ProjectFilter = projectName,
                    IncludePublic = false,
                    Limit = 50,
                    ExcludeEnums = false,
                    ExcludeRecordProperties = false,
                    ExcludeTestProjects = true,
                    ExcludeTests = true
                },
                ct).ConfigureAwait(false);
            var unusedSummary = unused.Take(20).Select(u =>
                $"- `{u.SymbolName}` ({u.SymbolKind}) in {u.FilePath}:{u.Line} — {u.ContainingType ?? "top-level"}").ToArray();

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Perform a dead code audit for this workspace.

                    **Project Filter:** {projectName ?? "(entire workspace)"}
                    **Unused Symbols Found:** {unused.Count}

                    **Sample Unused Symbols (first 20):**
                    {PromptMessageBuilder.FormatBulletList(unusedSummary, "No unused symbols detected.")}

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
            return [PromptMessageBuilder.CreateErrorMessage("dead_code_audit", ex)];
        }
    }

    [McpServerPrompt(Name = "review_test_coverage")]
    [Description("Generate a prompt that guides a test coverage review using test discovery, execution, and coverage tools.")]
    public static async Task<IEnumerable<PromptMessage>> ReviewTestCoverage(
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: specific test project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var discovered = await testDiscoveryService.DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
            var totalTests = discovered.TestProjects.Sum(p => p.Tests.Count);
            var perProject = Math.Max(1, 200 / Math.Max(1, discovered.TestProjects.Count));
            var truncatedProjects = discovered.TestProjects.Select(p =>
                new Core.Models.TestProjectDto(p.ProjectName, p.ProjectFilePath, p.Tests.Take(perProject).ToList())).ToList();
            var discoveredJson = JsonSerializer.Serialize(new Core.Models.TestDiscoveryDto(truncatedProjects), JsonDefaults.Indented);
            if (totalTests > 200)
                discoveredJson += $"\n[Showing ~200 of {totalTests} test cases]";

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
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
            return [PromptMessageBuilder.CreateErrorMessage("review_test_coverage", ex)];
        }
    }

    [McpServerPrompt(Name = "review_complexity")]
    [Description("Generate a prompt that guides a complexity review to identify and address high-complexity code.")]
    public static async Task<IEnumerable<PromptMessage>> ReviewComplexity(
        ICodeMetricsService codeMetricsService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var metrics = await codeMetricsService.GetComplexityMetricsAsync(workspaceId, filePath: null, filePaths: null, projectFilter: projectName, minComplexity: 5, limit: 50, ct).ConfigureAwait(false);
            var metricsJson = JsonSerializer.Serialize(metrics, JsonDefaults.Indented);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
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
            return [PromptMessageBuilder.CreateErrorMessage("review_complexity", ex)];
        }
    }

    [McpServerPrompt(Name = "cohesion_analysis")]
    [Description("Generate a prompt that guides SRP analysis using cohesion metrics, identifies extraction candidates, and plans type splits")]
    public static async Task<IEnumerable<PromptMessage>> CohesionAnalysis(
        ICohesionAnalysisService cohesionAnalysisService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        try
        {
            var metrics = await cohesionAnalysisService.GetCohesionMetricsAsync(
                workspaceId, filePath: null, projectFilter: projectName, minMethods: 3, limit: 20, includeInterfaces: false, excludeTestProjects: true, ct).ConfigureAwait(false);
            var metricsJson = JsonSerializer.Serialize(metrics, JsonDefaults.Indented);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Perform an SRP (Single Responsibility Principle) analysis using cohesion metrics.

                    **Project Filter:** {projectName ?? "(entire workspace)"}

                    **LCOM4 Cohesion Metrics (types with 3+ instance methods):**
                    ```json
                    {metricsJson}
                    ```

                    **How to interpret LCOM4:**
                    - **Score = 1**: Perfectly cohesive — all methods share state. No SRP issue.
                    - **Score = 2**: Two independent clusters — the type likely has two responsibilities.
                    - **Score >= 3**: Multiple independent clusters — strong SRP violation, prime extraction candidate.

                    **Recommended workflow for types with LCOM4 > 1:**
                    1. Call `find_shared_members` on the type to identify private members used by multiple public methods.
                       Shared members complicate extraction — they may need to be duplicated or extracted into a helper.
                    2. Call `find_consumers` on the type to understand who depends on it and how (constructor, field, parameter).
                       This determines the blast radius of splitting the type.
                    3. Use `extract_type_preview` to move one cluster's methods into a new focused type.
                    4. Use `extract_type_apply` to commit the extraction.
                    5. Optionally use `extract_interface_preview` to create an interface for the new type.
                    6. Use `bulk_replace_type_preview` to update consumers from the concrete type to the interface.
                    7. Call `build_workspace` to verify compilation after each step.
                    8. Call `test_related_files` and `test_run` to verify behavior.

                    Focus on types with the highest LCOM4 scores first — these have the most independent responsibilities.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [PromptMessageBuilder.CreateErrorMessage("cohesion_analysis", ex)];
        }
    }

    [McpServerPrompt(Name = "consumer_impact")]
    [Description("Generate a prompt that analyzes the consumer/dependency graph for a type to assess the impact of refactoring it")]
    public static async Task<IEnumerable<PromptMessage>> ConsumerImpact(
        IConsumerAnalysisService consumerAnalysisService,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        CancellationToken ct = default)
    {
        try
        {
            var locator = Core.Models.SymbolLocator.BySource(filePath, line, column);
            var result = await consumerAnalysisService.FindConsumersAsync(workspaceId, locator, ct).ConfigureAwait(false);
            if (result is null) return [PromptMessageBuilder.CreatePromptMessage("No symbol found at the specified location.")];

            var resultJson = PromptMessageBuilder.SerializeTruncatedList(result.Consumers, 50, JsonDefaults.Indented);

            return
            [
                PromptMessageBuilder.CreatePromptMessage($"""
                    Analyze the consumer/dependency graph for this type and assess refactoring impact.

                    **Consumer Analysis Results:**
                    ```json
                    {resultJson}
                    ```

                    **How to use this data:**
                    - **Constructor dependencies** indicate strong coupling — these consumers instantiate or receive the type via DI.
                    - **Field dependencies** indicate persistent coupling — the consumer stores and reuses the type.
                    - **Parameter dependencies** indicate transient coupling — the consumer only uses the type temporarily.
                    - **BaseType dependencies** indicate inheritance coupling — breaking changes are high-risk.

                    **Recommended next steps:**
                    1. If planning to extract an interface: call `extract_interface_preview` to create the interface.
                    2. Then use `bulk_replace_type_preview` with scope='parameters' to update consumers to use the interface.
                    3. For constructor-injected consumers, update their DI registrations.
                    4. Call `build_workspace` after each step to verify.
                    5. Use `impact_analysis` for additional reference-level detail on specific members.

                    **Risk assessment:** Types with many Constructor + Field consumers are the hardest to refactor safely.
                    """)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [PromptMessageBuilder.CreateErrorMessage("consumer_impact", ex)];
        }
    }
}

