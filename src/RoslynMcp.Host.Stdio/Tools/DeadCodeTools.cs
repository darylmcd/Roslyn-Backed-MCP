using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class DeadCodeTools
{

    [McpServerTool(Name = "remove_dead_code_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview removing unused symbols by symbol handle, optionally deleting files left empty by the removal.")]
    public static Task<string> PreviewRemoveDeadCode(
        IWorkspaceExecutionGate gate,
        IDeadCodeService deadCodeService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of stable symbol handles returned by semantic tools")] string[] symbolHandles,
        [Description("When true, delete files that become empty after removing the selected symbols")] bool removeEmptyFiles = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await deadCodeService.PreviewRemoveDeadCodeAsync(
                    workspaceId,
                    new DeadCodeRemovalDto(symbolHandles, removeEmptyFiles),
                    c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "remove_dead_code_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed dead-code removal operation.")]
    public static Task<string> ApplyRemoveDeadCode(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by remove_dead_code_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var gateKey = RefactoringTools.ApplyGateKeyFor(previewStore, previewToken);
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(gateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
