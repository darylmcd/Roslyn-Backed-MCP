using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class BulkRefactoringTools
{

    [McpServerTool(Name = "bulk_replace_type_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview replacing all references to one type with another across the solution. Scope can be 'parameters', 'fields', or 'all'. 'parameters' also covers generic arguments in implemented-interface / base-class declarations so the class's interface contract stays in sync. Useful after extracting an interface."),
     Description("Preview replacing all references to one type with another across the solution. Useful after extracting an interface to update all consumers. Scope can be 'parameters', 'fields', or 'all'. Under 'parameters' the replacement also walks generic arguments on implemented-interface / base-class declarations so the class's interface contract stays in sync with the rewritten parameter types.")]
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
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await bulkRefactoringService.PreviewBulkReplaceTypeAsync(workspaceId, oldTypeName, newTypeName, scope, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "bulk_replace_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previewed bulk type replacement. Updates all matching type references and adds using directives where needed."),
     Description("Apply a previously previewed bulk type replacement")]
    public static Task<string> ApplyBulkReplaceType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by bulk_replace_type_preview")] string previewToken,
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

    // replace-invocation-pattern-refactor: method-level ergonomic pair to bulk_replace_type_preview.
    // Rewrites every invocation of FQ.Old(a,b,c) to FQ.New(b,c,a) with argument reorder derived
    // from parameter-name matching between the two signatures. Apply reuses the existing
    // bulk_replace_type_apply for symmetry — both tools store a previewed Solution snapshot,
    // which the shared apply path redeems via the preview token.
    [McpServerTool(Name = "replace_invocation_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview rewriting every call-site of a method to call a different method with a declared argument-reorder mapping derived from parameter-name equality. Takes two FQ signatures and disambiguates overloads by parameter-type list."),
     Description("Preview rewriting every call-site of oldMethod with a call to newMethod whose parameter list is a permutation of oldMethod's. Supply fully-qualified signatures like 'Namespace.Type.Old(P1, P2, P3)' and 'Namespace.Type.New(P2, P3, P1)' — the parameter-type list disambiguates overloads; parameter-name equality (old P1 ↔ new P1) derives the reorder. Positional call sites are reordered; named-argument call sites keep their names and shuffle lexically into the new parameter order.")]
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
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await bulkRefactoringService.PreviewReplaceInvocationAsync(workspaceId, oldMethod, newMethod, scope, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
