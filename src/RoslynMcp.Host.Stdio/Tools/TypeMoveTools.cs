using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class TypeMoveTools
{

    [McpServerTool(Name = "move_type_to_file_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview moving a type declaration into its own file."),
     Description("Preview moving a type declaration from a multi-type file into its own dedicated file within the same project")]
    public static Task<string> PreviewMoveTypeToFile(
        IWorkspaceExecutionGate gate,
        ITypeMoveService typeMoveService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file containing the type")] string sourceFilePath,
        [Description("Name of the type to move")] string typeName,
        [Description("Optional: target file path. If omitted, defaults to {TypeName}.cs in the same directory")] string? targetFilePath = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await typeMoveService.PreviewMoveTypeToFileAsync(workspaceId, sourceFilePath, typeName, targetFilePath, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "move_type_to_file_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previewed move-type-to-file refactoring. Removes the type from the source file and creates its own dedicated file."),
     Description("Apply a previously previewed move-type-to-file refactoring")]
    public static Task<string> ApplyMoveTypeToFile(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by move_type_to_file_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var wsId = previewStore.PeekWorkspaceId(previewToken)
            ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
        return gate.RunWriteAsync(wsId, async c =>
        {
            var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "change_type_namespace_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview relocating a type between namespaces in the same project. Rewrites the type's namespace declaration, optionally moves the file, and adjusts consumer using directives respecting ambient-namespace resolution."),
     Description("Preview relocating a type between namespaces inside the same project. Rewrites the type's namespace declaration, optionally moves the file, and adjusts consumer using directives respecting ambient-namespace resolution. Pair with get_namespace_dependencies to break circular namespace dependencies.")]
    public static Task<string> PreviewChangeTypeNamespace(
        IWorkspaceExecutionGate gate,
        INamespaceRelocationService relocationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Name of the type to relocate (must be unique within fromNamespace)")] string typeName,
        [Description("Fully-qualified namespace currently containing the type")] string fromNamespace,
        [Description("Fully-qualified destination namespace inside the same project")] string toNamespace,
        [Description("Optional: absolute destination file path. If omitted, the file stays in place and only its namespace declaration is rewritten")] string? newFilePath = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await relocationService.PreviewChangeTypeNamespaceAsync(
                workspaceId, typeName, fromNamespace, toNamespace, newFilePath, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
