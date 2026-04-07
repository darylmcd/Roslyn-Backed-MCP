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
        return ToolErrorHandler.ExecuteAsync("scaffold_type_preview", () =>
        {
            ParameterValidation.ValidateTypeKind(typeKind);
            return gate.RunReadAsync(workspaceId, async c =>
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
        return ToolErrorHandler.ExecuteAsync("scaffold_type_apply", () =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "scaffold_test_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview scaffolding a new test file for a target type. Supports MSTest, xUnit, and NUnit (use testFramework or auto-detect from the test project's package references).")]
    public static Task<string> PreviewScaffoldTest(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Test project name or project file path within the loaded workspace")] string testProjectName,
        [Description("Target type name for the generated test")] string targetTypeName,
        [Description("Optional: target method name for the generated test stub")] string? targetMethodName = null,
        [Description("Test framework: mstest, xunit, nunit, or auto (infer from PackageReference in the test csproj)")] string testFramework = "auto",
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("scaffold_test_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await scaffoldingService.PreviewScaffoldTestAsync(
                    workspaceId,
                    new ScaffoldTestDto(testProjectName, targetTypeName, targetMethodName, testFramework),
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
        return ToolErrorHandler.ExecuteAsync("scaffold_test_apply", () =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
