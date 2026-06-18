using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ValidationTools
{

    [McpServerTool(Name = "build_workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for the loaded workspace and return structured diagnostics and execution output")]
    [McpToolMetadata("validation", "stable", false, false,
        "Run dotnet build for the loaded workspace.")]
    public static Task<string> BuildWorkspace(
        IWorkspaceExecutionGate gate,
        IBuildService buildService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        // build-workspace stage emissions: clients see "preparing-build → msbuild-running →
        // done" instead of waiting silently for an MSBuild target sweep across N projects.
        // Per-project N/M is intentionally not emitted — IBuildService doesn't accept
        // progress and adding it would require interface edits past the audit-coverage
        // initiative scope. See ProgressHelper remarks for the label-naming contract.
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ProgressHelper.ReportStage(progress, 0, 3, "preparing-build");
            ProgressHelper.ReportStage(progress, 1, 3, "msbuild-running");
            var result = await buildService.BuildWorkspaceAsync(workspaceId, c);
            ProgressHelper.ReportStage(progress, 3, 3, "done");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "build_project", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for a specific project in the loaded workspace and return structured diagnostics and execution output")]
    [McpToolMetadata("validation", "stable", false, false,
        "Run dotnet build for a selected project.")]
    public static Task<string> BuildProject(
        IWorkspaceExecutionGate gate,
        IBuildService buildService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => buildService.BuildProjectAsync(workspaceId, projectName, c),
            ct);

    [McpServerTool(Name = "test_discover", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Discover tests from test projects in the loaded workspace. Results are paginated to keep responses within MCP context budgets — large suites should be filtered with projectName and/or nameFilter. The response includes returnedCount/totalCount/hasMore so you can tell when more pages exist.")]
    [McpToolMetadata("validation", "stable", true, false,
        "Discover tests in the loaded workspace.")]
    public static Task<string> DiscoverTests(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: case-insensitive substring filter applied to fully-qualified test names")] string? nameFilter = null,
        [Description("Number of test cases to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum number of test cases to return (default: 200; raise carefully — 1000 cases is roughly 350KB of JSON)")] int limit = 200,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            if (limit <= 0)
                throw new ArgumentException("limit must be greater than 0.", nameof(limit));
            if (offset < 0)
                throw new ArgumentException("offset must be non-negative.", nameof(offset));

            var result = await testDiscoveryService.DiscoverTestsAsync(workspaceId, c);

            var filteredProjects = result.TestProjects.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                filteredProjects = filteredProjects.Where(p =>
                    string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
            }

            var projects = filteredProjects.ToList();

            // Apply name filter (substring, case-insensitive) BEFORE pagination so the offset
            // and limit reference filtered results, not raw discovery output.
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                projects = projects
                    .Select(p => new RoslynMcp.Core.Models.TestProjectDto(
                        p.ProjectName,
                        p.ProjectFilePath,
                        p.Tests
                            .Where(t => t.FullyQualifiedName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                            .ToList()))
                    .Where(p => p.Tests.Count > 0)
                    .ToList();
            }

            var totalAfterFilter = projects.Sum(p => p.Tests.Count);

            // Pagination: skip `offset` test cases (counted across projects), then take up to
            // `limit`. Empty projects after pagination are dropped.
            var remainingToSkip = offset;
            var remainingToTake = limit;
            var pagedProjects = new List<RoslynMcp.Core.Models.TestProjectDto>();
            foreach (var proj in projects)
            {
                if (remainingToTake <= 0) break;

                IEnumerable<RoslynMcp.Core.Models.TestCaseDto> tests = proj.Tests;
                if (remainingToSkip > 0)
                {
                    if (remainingToSkip >= proj.Tests.Count)
                    {
                        remainingToSkip -= proj.Tests.Count;
                        continue;
                    }
                    tests = tests.Skip(remainingToSkip);
                    remainingToSkip = 0;
                }

                var pagedTests = tests.Take(remainingToTake).ToList();
                if (pagedTests.Count == 0) continue;

                remainingToTake -= pagedTests.Count;
                pagedProjects.Add(new RoslynMcp.Core.Models.TestProjectDto(
                    proj.ProjectName, proj.ProjectFilePath, pagedTests));
            }

            var returnedCount = pagedProjects.Sum(p => p.Tests.Count);
            var hasMore = offset + returnedCount < totalAfterFilter;

            return JsonSerializer.Serialize(new
            {
                testProjects = pagedProjects,
                offset,
                limit,
                returnedCount,
                totalCount = totalAfterFilter,
                hasMore,
            }, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "test_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet test for the loaded workspace or a specific test project and return structured test results. When the run cannot produce TRX output (MSBuild file lock, build failure, timeout, unknown exit) the result carries a populated FailureEnvelope with ErrorKind ('FileLock'|'BuildFailure'|'Timeout'|'Unknown'), IsRetryable, Summary, and tails of StdOut/StdErr — instead of throwing a bare invocation error. Windows note: MSB3027/MSB3021 file-lock failures typically mean another testhost.exe (IDE test runner, background build) is holding the test assembly; the envelope classifies these as retryable so callers can close the conflicting runner and retry without touching source.")]
    [McpToolMetadata("validation", "stable", false, false,
        "Run dotnet test for the workspace or a selected project.")]
    public static Task<string> RunTests(
        IWorkspaceExecutionGate gate,
        ITestRunnerService testRunnerService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name or file path")] string? projectName = null,
        [Description("Optional: dotnet test filter expression")] string? filter = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        // test-run stage emissions: clients see "discovering-tests → running-tests → done"
        // instead of waiting silently while dotnet-test enumerates assemblies, builds the
        // host, runs the suite, and parses TRX. Per-project N/M is intentionally not
        // emitted — ITestRunnerService doesn't accept progress and adding it would require
        // interface edits past the audit-coverage initiative scope. See ProgressHelper
        // remarks for the label-naming contract.
        return gate.RunReadAsync(workspaceId, async c =>
        {
            try
            {
                ProgressHelper.ReportStage(progress, 0, 3, "discovering-tests");
                ProgressHelper.ReportStage(progress, 1, 3, "running-tests");
                var result = await testRunnerService.RunTestsAsync(workspaceId, projectName, filter, c);
                ProgressHelper.ReportStage(progress, 3, 3, "done");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ProgressHelper.ReportStage(progress, 3, 3, "done");
                var envelope = ToolErrorHandler.ClassifyAndFormat(ex, "test_run");
                return ToolErrorHandler.InjectSchemaHintIfPossible(envelope, "test_run");
            }
        }, ct);
    }

    [McpServerTool(Name = "test_related", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find likely related tests for a symbol by source location or symbol handle. Three mutually-exclusive locator modes: (1) symbolHandle alone — pass the stable handle returned by other semantic tools; (2) metadataName alone — pass a fully-qualified metadata name such as Namespace.TypeName; (3) source-location — pass filePath, line, AND column all together (all three are required when using this mode). Two-pass match: (1) heuristic name overlap (substring of the symbol name in test methods/classes — fast, catches the common case), plus (2) reference sweep via SymbolFinder.FindReferencesAsync over the symbol + its overrides/implementations (covers interface-dispatch tests that don't mention the interface name). An empty result set usually means: (a) the symbol's simple name doesn't appear in any test method/class name AND no test file references the symbol (or any implementation) by position, (b) the target symbol is a local/anonymous construct that isn't reachable by name. For file-based impact, use `test_related_files` instead.")]
    [McpToolMetadata("validation", "stable", true, false,
        "Find tests related to a symbol.")]
    public static Task<string> FindRelatedTests(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Source-location mode: absolute path to the source file. Must be provided together with line and column (all three are required as a group). Omit all three source-location parameters when using symbolHandle or metadataName instead.")] string? filePath = null,
        [Description("Source-location mode: 1-based line number. Must be provided together with filePath and column (all three are required as a group). Omit all three source-location parameters when using symbolHandle or metadataName instead.")] int? line = null,
        [Description("Source-location mode: 1-based column number pointing at the symbol identifier token (not the start of the line). Must be provided together with filePath and line (all three are required as a group). Note: this server uses 'column' (1-based), not the LSP-style 'character'. Omit all three source-location parameters when using symbolHandle or metadataName instead.")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("Maximum number of test cases to return (default: 100)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName);
            var result = await testDiscoveryService.FindRelatedTestsAsync(workspaceId, locator, maxResults, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "test_related_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Given a list of changed source file paths, find all related tests across the solution and return a combined dotnet test filter expression. Results use heuristic name matching and may not be exhaustive.")]
    [McpToolMetadata("validation", "stable", true, false,
        "Find tests related to a set of changed files.")]
    public static Task<string> FindRelatedTestsForFiles(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of absolute paths to changed source files")] string[] filePaths,
        [Description("Maximum number of test cases to return (default: 100)")] int maxResults = 100,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => testDiscoveryService.FindRelatedTestsForFilesAsync(workspaceId, filePaths, maxResults, c),
            ct);
}
