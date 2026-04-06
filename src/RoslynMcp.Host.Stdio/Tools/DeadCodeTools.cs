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
     Description("Preview removing unused symbols by symbol handle, optionally deleting files left empty by the removal. The removal preserves exterior trivia (XML doc comments, attributes attached to the next member); when removeEmptyFiles is true a file is deleted only if it has no real declarations left after the removal — empty namespace shells, using directives, and trivia all count as 'empty', but a file that still contains other types/methods is preserved (UX-005).")]
    public static Task<string> PreviewRemoveDeadCode(
        IWorkspaceExecutionGate gate,
        IDeadCodeService deadCodeService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of stable symbol handles returned by semantic tools")] string[] symbolHandles,
        [Description("When true, delete files whose remaining content is purely trivia or empty namespace shells after the removal (UX-005). False keeps the file and instead emits a warning")] bool removeEmptyFiles = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
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
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }
}
