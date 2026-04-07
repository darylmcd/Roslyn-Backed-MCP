using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class MSBuildTools
{
    [McpServerTool(Name = "evaluate_msbuild_property", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Evaluate a single MSBuild property for a project (e.g. TargetFramework, Nullable) using Microsoft.Build.Evaluation.")]
    public static Task<string> EvaluateMsbuildProperty(
        IWorkspaceExecutionGate gate,
        IMsBuildEvaluationService msbuildEvaluation,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name as loaded in the workspace")] string project,
        [Description("Property name (e.g. TargetFramework)")] string propertyName,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("evaluate_msbuild_property", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await msbuildEvaluation.EvaluatePropertyAsync(workspaceId, project, propertyName, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "evaluate_msbuild_items", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("List MSBuild items of a given type (e.g. Compile, PackageReference) with evaluated includes and metadata.")]
    public static Task<string> EvaluateMsbuildItems(
        IWorkspaceExecutionGate gate,
        IMsBuildEvaluationService msbuildEvaluation,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name as loaded in the workspace")] string project,
        [Description("Item type (e.g. Compile, PackageReference)")] string itemType,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("evaluate_msbuild_items", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await msbuildEvaluation.EvaluateItemsAsync(workspaceId, project, itemType, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_msbuild_properties", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Dump evaluated MSBuild properties for a project. The full set is large (frequently 60KB+ of mostly internal MSBuild state); always pass a propertyNameFilter substring or an explicit includedNames allowlist unless you really need everything (BUG-008). The response includes totalCount/returnedCount/appliedFilter for visibility.")]
    public static Task<string> GetMsbuildProperties(
        IWorkspaceExecutionGate gate,
        IMsBuildEvaluationService msbuildEvaluation,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name as loaded in the workspace")] string project,
        [Description("Optional: case-insensitive substring filter applied to property names (e.g., 'Nullable', 'Target')")] string? propertyNameFilter = null,
        [Description("Optional: explicit allowlist of property names to return. Takes precedence over propertyNameFilter when supplied.")] string[]? includedNames = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("get_msbuild_properties", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await msbuildEvaluation.GetEvaluatedPropertiesAsync(
                    workspaceId, project, propertyNameFilter, includedNames, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
