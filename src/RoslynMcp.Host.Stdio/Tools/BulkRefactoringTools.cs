using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class BulkRefactoringTools
{

    [McpServerTool(Name = "bulk_replace_type_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Preview replacing all references to one type with another across the solution. Useful after extracting an interface to update all consumers. Scope can be 'parameters', 'fields', or 'all'.")]
    public static Task<string> PreviewBulkReplaceType(
        IWorkspaceExecutionGate gate,
        IBulkRefactoringService bulkRefactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Fully qualified or simple name of the type to replace")] string oldTypeName,
        [Description("Fully qualified or simple name of the replacement type")] string newTypeName,
        [Description("Optional: scope filter — 'parameters', 'fields', or 'all' (default: 'all')")] string? scope = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            ParameterValidation.ValidateBulkReplaceScope(scope);
            return gate.RunAsync(workspaceId, async c =>
            {
                var result = await bulkRefactoringService.PreviewBulkReplaceTypeAsync(workspaceId, oldTypeName, newTypeName, scope, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "bulk_replace_type_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed bulk type replacement")]
    public static Task<string> ApplyBulkReplaceType(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by bulk_replace_type_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
