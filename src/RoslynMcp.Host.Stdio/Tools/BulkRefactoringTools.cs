using System.ComponentModel;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for bulk type-replacement and invocation-rewrite refactorings.
/// WS1 phase 1.3 — each shim body delegates to the corresponding <see cref="ToolDispatch"/>
/// helper instead of carrying the 7-line dispatch boilerplate inline. See
/// <c>CodeActionTools</c> (canary, PR #305) and <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>
/// for the migration rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class BulkRefactoringTools
{
    /// <remarks>
    /// Scope may be <c>parameters</c>, <c>fields</c>, or <c>all</c>. Parameter scope also walks
    /// generic arguments in implemented-interface and base-class declarations so the type's
    /// contract remains aligned with rewritten parameter types.
    /// </remarks>
    [McpServerTool(Name = "bulk_replace_type_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview replacing all references to one type with another across the solution. Scope can be 'parameters', 'fields', or 'all'. 'parameters' also covers generic arguments in implemented-interface / base-class declarations so the class's interface contract stays in sync. Useful after extracting an interface."),
     Description("Preview replacing one type across solution consumers after interface extraction. Choose parameters, fields, or all; parameters also updates generic base/interface arguments.")]
    public static Task<string> PreviewBulkReplaceType(
        IWorkspaceExecutionGate gate,
        IBulkRefactoringService bulkRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified or simple name of the type to replace")] string oldTypeName,
        [Description("Fully qualified or simple name of the replacement type")] string newTypeName,
        [Description("Optional: scope filter — 'parameters', 'fields', or 'all' (default: 'all')")] string? scope = null,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateBulkReplaceScope(scope);
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => bulkRefactoringService.PreviewBulkReplaceTypeAsync(workspaceId, oldTypeName, newTypeName, scope, c),
            ct);
    }

    /// <remarks>
    /// Both supported preview routes store the same preview kind. Producer-family and workspace
    /// provenance are validated before the shared apply path mutates the workspace.
    /// </remarks>
    [McpServerTool(Name = "bulk_replace_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previewed bulk type replacement or invocation rewrite. Redeems tokens from bulk_replace_type_preview and replace_invocation_preview."),
     Description("Apply a token from bulk_replace_type_preview or replace_invocation_preview. This shared route rejects other preview families before mutating the workspace.")]
    public static Task<string> ApplyBulkReplaceType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by bulk_replace_type_preview or replace_invocation_preview")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "bulk_replace_type_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            // Both bulk_replace_type_preview and replace_invocation_preview mint this kind — they
            // deliberately share this single apply route.
            expectedKind: PreviewKind.BulkReplaceType,
            invokedRoute: "bulk_replace_type_apply");

    // replace-invocation-pattern-refactor: method-level ergonomic pair to bulk_replace_type_preview.
    // Rewrites every invocation of FQ.Old(a,b,c) to FQ.New(b,c,a) with argument reorder derived
    // from parameter-name matching between the two signatures. Apply reuses the existing
    // bulk_replace_type_apply for symmetry — both tools store a previewed Solution snapshot,
    // which the shared apply path redeems via the preview token.
    /// <remarks>
    /// Supply fully-qualified signatures with parameter types to disambiguate overloads.
    /// Parameter-name equality derives the reorder: positional arguments move, while named
    /// arguments retain their names and are ordered to match the replacement signature.
    /// </remarks>
    [McpServerTool(Name = "replace_invocation_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview rewriting every call-site of a method to call a different method with a declared argument-reorder mapping derived from parameter-name equality. Redeem the token with bulk_replace_type_apply."),
     Description("Preview rewriting every oldMethod call to newMethod by matching parameter names and reordering arguments. Redeem the returned token with bulk_replace_type_apply.")]
    public static Task<string> PreviewReplaceInvocation(
        IWorkspaceExecutionGate gate,
        IBulkRefactoringService bulkRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully-qualified signature of the method being replaced: 'Namespace.Type.OldMethod(ParamType1, ParamType2)'. Parameter-type list disambiguates overloads.")] string oldMethod,
        [Description("Fully-qualified signature of the replacement method: 'Namespace.Type.NewMethod(ParamTypeA, ParamTypeB)'. Parameter names (matched against oldMethod's) drive the argument reorder.")] string newMethod,
        [Description("Optional: scope filter — reserved for future use (currently only 'all' is supported).")] string? scope = null,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateReplaceInvocationScope(scope);
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => bulkRefactoringService.PreviewReplaceInvocationAsync(workspaceId, oldMethod, newMethod, scope, c),
            ct);
    }
}
