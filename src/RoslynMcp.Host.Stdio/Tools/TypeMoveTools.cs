using System.ComponentModel;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for Roslyn type-relocation refactorings (move-type-to-file,
/// change-type-namespace). WS1 phase 1.4 — each shim body delegates to the
/// corresponding <see cref="ToolDispatch"/> helper instead of carrying the 7-line
/// dispatch boilerplate inline. See <c>CodeActionTools</c> (canary, PR #305),
/// <c>BulkRefactoringTools</c> (phase 1.3), and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the migration
/// rationale and the deferred-generator blocker.
/// </summary>
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
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => typeMoveService.PreviewMoveTypeToFileAsync(workspaceId, sourceFilePath, typeName, targetFilePath, c),
            ct);

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
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "move_type_to_file_apply", c),
            ct);

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
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => relocationService.PreviewChangeTypeNamespaceAsync(
                workspaceId, typeName, fromNamespace, toNamespace, newFilePath, c),
            ct);
}
