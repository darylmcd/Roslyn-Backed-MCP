using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ValidationTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "build_workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for the loaded workspace and return structured diagnostics and execution output")]
    public static Task<string> BuildWorkspace(
        IWorkspaceExecutionGate gate,
        IBuildService buildService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var result = await buildService.BuildWorkspaceAsync(workspaceId, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonOptions);
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
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await buildService.BuildProjectAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_discover", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Discover tests from test projects in the loaded workspace")]
    public static Task<string> DiscoverTests(
        IWorkspaceExecutionGate gate,
        ITestDiscoveryService testDiscoveryService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await testDiscoveryService.DiscoverTestsAsync(workspaceId, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet test for the loaded workspace or a specific test project and return structured test results")]
    public static Task<string> RunTests(
        IWorkspaceExecutionGate gate,
        ITestRunnerService testRunnerService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name or file path")] string? projectName = null,
        [Description("Optional: dotnet test filter expression")] string? filter = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var result = await testRunnerService.RunTestsAsync(workspaceId, projectName, filter, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_related", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find likely related tests for a symbol by source location or symbol handle. Results use heuristic name matching and may not be exhaustive.")]
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
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var locator = SymbolLocatorFactory.Create(filePath, line, column, symbolHandle);
                var result = await testDiscoveryService.FindRelatedTestsAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonOptions);
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
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await testDiscoveryService.FindRelatedTestsForFilesAsync(workspaceId, filePaths, maxResults, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}
