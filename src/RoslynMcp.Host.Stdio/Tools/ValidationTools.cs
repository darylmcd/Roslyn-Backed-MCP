using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ValidationTools
{

    [McpServerTool(Name = "build_workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for the loaded workspace and return structured diagnostics and execution output")]
    public static Task<string> BuildWorkspace(
        IWorkspaceExecutionGate gate,
        IBuildService buildService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("build_workspace", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var result = await buildService.BuildWorkspaceAsync(workspaceId, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "build_project", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for a specific project in the loaded workspace and return structured diagnostics and execution output")]
    public static Task<string> BuildProject(
        IWorkspaceExecutionGate gate,
        IBuildService buildService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("build_project", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await buildService.BuildProjectAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "test_discover", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Discover tests from test projects in the loaded workspace. Results are paginated to keep responses within MCP context budgets — large suites should be filtered with projectName and/or nameFilter (BUG-007). The response includes returnedCount/totalCount/hasMore so you can tell when more pages exist.")]
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
        return ToolErrorHandler.ExecuteAsync("test_discover", () =>
            gate.RunReadAsync(workspaceId, async c =>
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
            }, ct));
    }

    [McpServerTool(Name = "test_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet test for the loaded workspace or a specific test project and return structured test results. When the run cannot produce TRX output (MSBuild file lock, build failure, timeout, unknown exit) the result carries a populated FailureEnvelope with ErrorKind ('FileLock'|'BuildFailure'|'Timeout'|'Unknown'), IsRetryable, Summary, and tails of StdOut/StdErr — instead of throwing a bare invocation error. Windows note: MSB3027/MSB3021 file-lock failures typically mean another testhost.exe (IDE test runner, background build) is holding the test assembly; the envelope classifies these as retryable so callers can close the conflicting runner and retry without touching source.")]
    public static Task<string> RunTests(
        IWorkspaceExecutionGate gate,
        ITestRunnerService testRunnerService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name or file path")] string? projectName = null,
        [Description("Optional: dotnet test filter expression")] string? filter = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("test_run", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var result = await testRunnerService.RunTestsAsync(workspaceId, projectName, filter, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "test_related", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find likely related tests for a symbol by source location or symbol handle. Results use heuristic name matching (substring of the symbol name in test methods/classes) and may not be exhaustive. An empty result set usually means: (a) the symbol's simple name doesn't appear as a substring in any test method/class name (common for private/internal symbols), (b) the target symbol is a local/anonymous construct that isn't reachable by name, or (c) tests reference the symbol only through an interface and no test name contains that interface's name. For file-based impact, use `test_related_files` instead.")]
    public static Task<string> FindRelatedTests(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("test_related", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false);
                var result = await testDiscoveryService.FindRelatedTestsAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "test_related_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Given a list of changed source file paths, find all related tests across the solution and return a combined dotnet test filter expression. Results use heuristic name matching and may not be exhaustive.")]
    public static Task<string> FindRelatedTestsForFiles(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of absolute paths to changed source files")] string[] filePaths,
        [Description("Maximum number of test cases to return (default: 100)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("test_related_files", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await testDiscoveryService.FindRelatedTestsForFilesAsync(workspaceId, filePaths, maxResults, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
