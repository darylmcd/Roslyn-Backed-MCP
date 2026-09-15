using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP entry points for moving a type into its own file and changing its namespace.
/// Preview calls use the workspace gate; file moves also validate the source path
/// against client roots. Apply calls use <see cref="ToolDispatch"/> to validate token provenance.
/// </summary>
[McpServerToolType]
public static class TypeMoveTools
{
    /// <remarks>
    /// The source file must contain at least two top-level types. Use <c>move_file_preview</c>
    /// for single-type rename or move operations.
    /// </remarks>
    [McpServerTool(Name = "move_type_to_file_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview moving a type declaration into its own file."),
     Description("Preview moving a declaration from a multi-type source file into its own file in the same project. The source must contain at least two top-level types; use move_file_preview for single-type moves.")]
    public static Task<string> PreviewMoveTypeToFile(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ITypeMoveService typeMoveService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Absolute path to the source file containing the type.")] string sourceFilePath,
        [Description("Name of the type to move")] string typeName,
        [Description("Optional: target file path. If omitted, defaults to {TypeName}.cs in the same directory")] string? targetFilePath = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, sourceFilePath, c).ConfigureAwait(false);
            var dto = await typeMoveService.PreviewMoveTypeToFileAsync(workspaceId, sourceFilePath, typeName, targetFilePath, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
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
        [Description("Preview token from move_type_to_file_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "move_type_to_file_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.MoveTypeToFile,
            invokedRoute: "move_type_to_file_apply");

    /// <remarks>
    /// Use <c>get_namespace_dependencies</c> to identify circular namespace dependencies before
    /// relocating a type.
    /// </remarks>
    [McpServerTool(Name = "change_type_namespace_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview relocating a type between namespaces in the same project. Rewrites the type's namespace declaration, optionally moves the file, and adjusts consumer using directives respecting ambient-namespace resolution."),
     Description("Preview relocating a type between namespaces in one project, optionally moving its file and updating consumer using directives. Use get_namespace_dependencies to identify circular dependencies.")]
    public static Task<string> PreviewChangeTypeNamespace(
        IWorkspaceExecutionGate gate,
        INamespaceRelocationService relocationService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Name of the type to relocate (must be unique within fromNamespace)")] string typeName,
        [Description("Fully-qualified namespace currently containing the type")] string fromNamespace,
        [Description("Fully-qualified destination namespace inside the same project")] string toNamespace,
        [Description("Optional: absolute destination file path. If omitted, the file stays in place and only its namespace declaration is rewritten")] string? newFilePath = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => relocationService.PreviewChangeTypeNamespaceAsync(
                workspaceId, typeName, fromNamespace, toNamespace, newFilePath, c),
            ct);
}
