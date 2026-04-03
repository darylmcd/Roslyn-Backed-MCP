using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ScaffoldingTools
{

    [McpServerTool(Name = "scaffold_type_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview scaffolding a new type file in a project.")]
    public static Task<string> PreviewScaffoldType(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Type name to create")] string typeName,
        [Description("Type kind: class, interface, record, or enum")] string typeKind = "class",
        [Description("Optional: namespace override")] string? @namespace = null,
        [Description("Optional: base type name")] string? baseType = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            ParameterValidation.ValidateTypeKind(typeKind);
            return gate.RunAsync(workspaceId, async c =>
            {
                var result = await scaffoldingService.PreviewScaffoldTypeAsync(
                    workspaceId,
                    new ScaffoldTypeDto(projectName, typeName, typeKind, @namespace, baseType),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "scaffold_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed type scaffolding operation.")]
    public static Task<string> ApplyScaffoldType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by scaffold_type_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "scaffold_test_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview scaffolding a new MSTest file for a target type.")]
    public static Task<string> PreviewScaffoldTest(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Test project name or project file path within the loaded workspace")] string testProjectName,
        [Description("Target type name for the generated test")] string targetTypeName,
        [Description("Optional: target method name for the generated test stub")] string? targetMethodName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await scaffoldingService.PreviewScaffoldTestAsync(
                    workspaceId,
                    new ScaffoldTestDto(testProjectName, targetTypeName, targetMethodName),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "scaffold_test_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed test scaffolding operation.")]
    public static Task<string> ApplyScaffoldTest(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by scaffold_test_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
