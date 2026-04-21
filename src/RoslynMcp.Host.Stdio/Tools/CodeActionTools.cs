using System.ComponentModel;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for Roslyn code-action operations. WS1 phase 1.2 canary —
/// each shim body delegates to the corresponding <see cref="ToolDispatch"/> helper
/// instead of carrying the 7-line dispatch boilerplate inline.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generator-migration blocker recorded:</b> the phase-1.2 plan called for
/// <see cref="McpToolShimGenerator"/> to emit the bodies from
/// <c>[GeneratedDispatch]</c>-annotated <c>partial</c> declarations. That approach
/// collides with <c>ModelContextProtocol.Analyzers.XmlToDescriptionGenerator</c>
/// (shipped in the MCP SDK), which ALSO emits <c>public static partial</c>
/// declarations for every <c>[McpServerTool]</c> method — CS0756 "A partial method
/// may not have multiple defining declarations" fires when both generators target
/// the same tool class. Until that conflict is resolved (phase 1.3+ problem — talk
/// to the MCP SDK maintainers about a declarative opt-out, or have our generator
/// emit into a non-<c>partial</c> sibling helper class), the canary keeps the
/// hand-written shim shape and the win is strictly the LOC reduction from using
/// <see cref="ToolDispatch"/> inline. See
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the full plan.
/// </para>
/// <para>
/// The <see cref="ICodeActionService.GetCodeActionsAsync"/> result type changed from
/// <c>IReadOnlyList&lt;CodeActionDto&gt;</c> to <see cref="RoslynMcp.Core.Models.CodeActionListDto"/>
/// in this PR so the empty-result hint (FLAG-6B) moves out of the Tool shim and into
/// the service — making every tool body in this file a pure dispatch. This is the
/// prerequisite architectural shape the generator migration will eventually consume.
/// </para>
/// </remarks>
[McpServerToolType]
public static class CodeActionTools
{
    [McpServerTool(Name = "get_code_actions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get available Roslyn code fixes and refactorings at a position or selection range in a source file")]
    [McpToolMetadata("code-actions", "stable", true, false,
        "List Roslyn code fixes and refactorings at a location or selection range. Selection-range refactorings include introduce parameter and inline temporary variable. Pass endLine/endColumn for selection-range actions.")]
    public static Task<string> GetCodeActions(
        IWorkspaceExecutionGate gate,
        ICodeActionService codeActionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line number")] int startLine,
        [Description("1-based start column number")] int startColumn,
        [Description("Optional: 1-based end line number for a selection range")] int? endLine = null,
        [Description("Optional: 1-based end column number for a selection range")] int? endColumn = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => codeActionService.GetCodeActionsAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, c),
            ct);

    [McpServerTool(Name = "preview_code_action", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview the changes that a specific code action would make. Use get_code_actions first to get the available actions and their indices.")]
    [McpToolMetadata("code-actions", "stable", true, false,
        "Preview a Roslyn code action before applying it.")]
    public static Task<string> PreviewCodeAction(
        IWorkspaceExecutionGate gate,
        ICodeActionService codeActionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line number")] int startLine,
        [Description("1-based start column number")] int startColumn,
        [Description("The index of the code action to preview (from get_code_actions result)")] int actionIndex,
        [Description("Optional: 1-based end line number for a selection range")] int? endLine = null,
        [Description("Optional: 1-based end column number for a selection range")] int? endColumn = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => codeActionService.PreviewCodeActionAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, actionIndex, c),
            ct);

    [McpServerTool(Name = "apply_code_action", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code action using its preview token")]
    [McpToolMetadata("code-actions", "stable", false, true,
        "Apply a previously previewed Roslyn code action.")]
    public static Task<string> ApplyCodeAction(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by preview_code_action")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, c),
            ct);
}
