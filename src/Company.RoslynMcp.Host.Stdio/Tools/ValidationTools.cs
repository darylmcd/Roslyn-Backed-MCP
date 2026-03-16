using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ValidationTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "build_workspace", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for the loaded workspace and return structured diagnostics and execution output")]
    public static Task<string> BuildWorkspace(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                var result = await validationService.BuildWorkspaceAsync(workspaceId, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "build_project", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet build for a specific project in the loaded workspace and return structured diagnostics and execution output")]
    public static Task<string> BuildProject(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await validationService.BuildProjectAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_discover", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Discover tests from test projects in the loaded workspace")]
    public static Task<string> DiscoverTests(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await validationService.DiscoverTestsAsync(workspaceId, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_run", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Run dotnet test for the loaded workspace or a specific test project and return structured test results")]
    public static Task<string> RunTests(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
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
                var result = await validationService.RunTestsAsync(workspaceId, projectName, filter, c);
                ProgressHelper.Report(progress, 1, 1);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_related", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Find likely related tests for a symbol by source location or symbol handle")]
    public static Task<string> FindRelatedTests(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
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
                var locator = CreateLocator(filePath, line, column, symbolHandle);
                var result = await validationService.FindRelatedTestsAsync(workspaceId, locator, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "test_related_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Given a list of changed source file paths, find all related tests across the solution and return a combined dotnet test filter expression")]
    public static Task<string> FindRelatedTestsForFiles(
        IWorkspaceExecutionGate gate,
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of absolute paths to changed source files")] string[] filePaths,
        [Description("Maximum number of test cases to return (default: 100)")] int maxResults = 100,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await validationService.FindRelatedTestsForFilesAsync(workspaceId, filePaths, maxResults, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    private static SymbolLocator CreateLocator(string? filePath, int? line, int? column, string? symbolHandle)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
        {
            return SymbolLocator.ByHandle(symbolHandle);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            return SymbolLocator.BySource(filePath, line.Value, column.Value);
        }

        throw new ArgumentException("Provide either filePath/line/column or symbolHandle.");
    }
}
