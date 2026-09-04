using System.ComponentModel;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for fix-all-instances diagnostic remediation. WS1 phase 1.3 —
/// each shim body delegates to the corresponding <see cref="ToolDispatch"/> helper
/// instead of carrying the 7-line dispatch boilerplate inline. See <c>CodeActionTools</c>
/// (canary, PR #305) and <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>
/// for the migration rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class FixAllTools
{
    /// <remarks>
    /// Scope is document, project, or solution. Discover diagnostic ids with
    /// <c>list_analyzers</c> or <c>project_diagnostics</c>. When no provider or Fix All support
    /// exists, <c>guidanceMessage</c> names the next route. Provider crashes return a structured
    /// <c>FixAllProviderCrash</c> envelope and state whether per-occurrence fallback is available.
    /// </remarks>
    [McpServerTool(Name = "fix_all_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview fixing ALL instances of a diagnostic across a scope."),
     Description("Preview one Fix All provider across document, project, or solution scope. Use after list_analyzers or project_diagnostics; the result identifies per-occurrence code_fix_preview fallback when needed.")]
    public static Task<string> PreviewFixAll(
        IWorkspaceExecutionGate gate,
        IFixAllService fixAllService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier to fix everywhere, e.g. CS8019, IDE0005")] string diagnosticId,
        [Description("Scope of the fix: 'document', 'project', or 'solution'")] string scope,
        [Description("Required when scope is 'document': absolute path to the source file")] string? filePath = null,
        [Description("Required when scope is 'project': the project name")] string? projectName = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => fixAllService.PreviewFixAllAsync(workspaceId, diagnosticId, scope, filePath, projectName, c),
            ct);

    /// <remarks>
    /// The preview token records its producer family and workspace. Tokens minted by unrelated
    /// preview routes are rejected before any mutation.
    /// </remarks>
    [McpServerTool(Name = "fix_all_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previously previewed fix-all operation."),
     Description("Apply a fix-all preview token across its recorded scope. Use only after fix_all_preview; tokens from unrelated preview families are rejected before workspace mutation.")]
    public static Task<string> ApplyFixAll(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by fix_all_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "fix_all_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.FixAll,
            invokedRoute: "fix_all_apply");
}
