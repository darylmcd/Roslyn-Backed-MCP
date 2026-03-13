using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ValidationTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "build_workspace"), Description("Run dotnet build for the loaded workspace and return structured diagnostics and execution output")]
    public static async Task<string> BuildWorkspace(
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        var result = await validationService.BuildWorkspaceAsync(workspaceId, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "build_project"), Description("Run dotnet build for a specific project in the loaded workspace and return structured diagnostics and execution output")]
    public static async Task<string> BuildProject(
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        CancellationToken ct = default)
    {
        var result = await validationService.BuildProjectAsync(workspaceId, projectName, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "test_discover"), Description("Discover tests from test projects in the loaded workspace")]
    public static async Task<string> DiscoverTests(
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        var result = await validationService.DiscoverTestsAsync(workspaceId, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "test_run"), Description("Run dotnet test for the loaded workspace or a specific test project and return structured test results")]
    public static async Task<string> RunTests(
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: specific test project name or file path")] string? projectName = null,
        [Description("Optional: dotnet test filter expression")] string? filter = null,
        CancellationToken ct = default)
    {
        var result = await validationService.RunTestsAsync(workspaceId, projectName, filter, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "test_related"), Description("Find likely related tests for a symbol by source location or symbol handle")]
    public static async Task<string> FindRelatedTests(
        IValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: absolute path to the source file")] string? filePath = null,
        [Description("Optional: 1-based line number")] int? line = null,
        [Description("Optional: 1-based column number")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var locator = CreateLocator(filePath, line, column, symbolHandle);
        var result = await validationService.FindRelatedTestsAsync(workspaceId, locator, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
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
