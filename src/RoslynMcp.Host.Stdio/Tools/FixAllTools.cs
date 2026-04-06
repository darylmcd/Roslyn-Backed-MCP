using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class FixAllTools
{
    [McpServerTool(Name = "fix_all_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview applying a code fix to ALL instances of a diagnostic across a scope (document, project, or solution). Dramatically faster than fixing diagnostics one at a time. Use list_analyzers or project_diagnostics to find diagnostic IDs. When no provider or no FixAll support exists, the response includes guidanceMessage with next steps (e.g. organize_usings_preview for IDE0005).")]
    public static Task<string> PreviewFixAll(
        IWorkspaceExecutionGate gate,
        IFixAllService fixAllService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier to fix everywhere, e.g. CS8019, IDE0005")] string diagnosticId,
        [Description("Scope of the fix: 'document', 'project', or 'solution'")] string scope,
        [Description("Required when scope is 'document': absolute path to the source file")] string? filePath = null,
        [Description("Optional: project name when scope is 'project'")] string? projectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await fixAllService.PreviewFixAllAsync(workspaceId, diagnosticId, scope, filePath, projectName, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "fix_all_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed fix-all operation using its preview token")]
    public static Task<string> ApplyFixAll(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by fix_all_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
