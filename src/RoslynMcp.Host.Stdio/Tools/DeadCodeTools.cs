using System.ComponentModel;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for dead-code removal. WS1 phase 1.6 — each shim body
/// delegates to the corresponding <see cref="ToolDispatch"/> helper instead of
/// carrying the dispatch boilerplate inline. See <c>CodeActionTools</c> (canary,
/// PR #305) and <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>
/// for the migration rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class DeadCodeTools
{

    /// <remarks>
    /// When <c>removeEmptyFiles</c> is true, a file is deleted only when it has no real
    /// declarations after removal; empty namespace shells, using directives, and trivia count as
    /// empty while files containing other types or methods remain intact (UX-005).
    /// </remarks>
    [McpServerTool(Name = "remove_dead_code_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("dead-code", "stable", true, false,
        "Preview removing unused symbols by handle."),
     Description("Preview removal of unused symbols by handle, with optional cleanup of files left empty. When removeEmptyFiles is true, only files without real declarations are deleted (UX-005).")]
    public static Task<string> PreviewRemoveDeadCode(
        IWorkspaceExecutionGate gate,
        IDeadCodeService deadCodeService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of stable symbol handles returned by semantic tools")] string[] symbolHandles,
        [Description("When true, delete files whose remaining content is purely trivia or empty namespace shells after the removal (UX-005). False keeps the file and instead emits a warning")] bool removeEmptyFiles = false,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => deadCodeService.PreviewRemoveDeadCodeAsync(
                workspaceId,
                new DeadCodeRemovalDto(symbolHandles, removeEmptyFiles),
                c),
            ct);

    [McpServerTool(Name = "remove_dead_code_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("dead-code", "experimental", false, true,
        "Apply a previously previewed dead-code removal operation."),
     Description("Apply a previously previewed dead-code removal operation.")]
    public static Task<string> ApplyRemoveDeadCode(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by remove_dead_code_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "remove_dead_code_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.RemoveDeadCode,
            invokedRoute: "remove_dead_code_apply");
}
