using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ScaffoldingTools
{

    [McpServerTool(Name = "scaffold_type_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview scaffolding a new type file in a project. When baseType or interfaces resolve to an interface and implementInterface is true (default), interface members are emitted as NotImplementedException stubs so the scaffolded class compiles against the interface contract.")]
    public static Task<string> PreviewScaffoldType(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Project name or project file path within the loaded workspace")] string projectName,
        [Description("Type name to create")] string typeName,
        [Description("Type kind: class, interface, record, or enum")] string typeKind = "class",
        [Description("Optional: namespace override")] string? @namespace = null,
        [Description("Optional: base type name")] string? baseType = null,
        [Description("Optional: additional interface names to declare on the scaffolded type")] string[]? interfaces = null,
        [Description("When true (default), auto-implement interface members of baseType/interfaces as NotImplementedException stubs; set false to emit an empty class body")] bool implementInterface = true,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("scaffold_type_preview", () =>
        {
            ParameterValidation.ValidateTypeKind(typeKind);
            return gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await scaffoldingService.PreviewScaffoldTypeAsync(
                    workspaceId,
                    new ScaffoldTypeDto(projectName, typeName, typeKind, @namespace, baseType, interfaces, implementInterface),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "scaffold_test_batch_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("scaffolding", "experimental", true, false,
        "Preview scaffolding multiple test files for related target types in one composite preview."),
     Description("Preview scaffolding test files for multiple target types in a single composite preview. Reuses one workspace snapshot across targets to avoid per-target compilation cost. Apply via apply_composite_preview or the returned token.")]
    public static Task<string> PreviewScaffoldTestBatch(
        IWorkspaceExecutionGate gate,
        IScaffoldingService scaffoldingService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Test project name or project file path within the loaded workspace")] string testProjectName,
        [Description("Array of targets. Each item: { targetTypeName: string, targetMethodName?: string }")] ScaffoldTestBatchTargetDto[] targets,
        [Description("Test framework: mstest, xunit, nunit, or auto (default — inferred from the test project's PackageReferences)")] string testFramework = "auto",
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("scaffold_test_batch_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await scaffoldingService.PreviewScaffoldTestBatchAsync(
                    workspaceId,
                    new ScaffoldTestBatchDto(testProjectName, targets ?? [], testFramework),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
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
