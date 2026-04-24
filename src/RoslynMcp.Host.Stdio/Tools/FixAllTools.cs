using System.ComponentModel;
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
    [McpServerTool(Name = "fix_all_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview fixing ALL instances of a diagnostic across a scope."),
     Description("Preview applying a code fix to ALL instances of a diagnostic across a scope (document, project, or solution). Dramatically faster than fixing diagnostics one at a time. Use list_analyzers or project_diagnostics to find diagnostic IDs. When no provider or no FixAll support exists, the response includes guidanceMessage with next steps (e.g. organize_usings_preview for IDE0005). When the registered FixAll provider itself throws (e.g. the 'Sequence contains no elements' crash on IDE0300), the response is a structured envelope with error=true, category='FixAllProviderCrash', and perOccurrenceFallbackAvailable=true so callers can fall back to code_fix_preview on individual occurrences.")]
    public static Task<string> PreviewFixAll(
        IWorkspaceExecutionGate gate,
        IFixAllService fixAllService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier to fix everywhere, e.g. CS8019, IDE0005")] string diagnosticId,
        [Description("Scope of the fix: 'document', 'project', or 'solution'")] string scope,
        [Description("Required when scope is 'document': absolute path to the source file")] string? filePath = null,
        [Description("Optional: project name when scope is 'project'")] string? projectName = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => fixAllService.PreviewFixAllAsync(workspaceId, diagnosticId, scope, filePath, projectName, c),
            ct);

    [McpServerTool(Name = "fix_all_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previously previewed fix-all operation."),
     Description("Apply a previously previewed fix-all operation using its preview token")]
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
            c => refactoringService.ApplyRefactoringAsync(previewToken, c),
            ct);
}
