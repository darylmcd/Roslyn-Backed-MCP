using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class MultiFileEditTools
{

    [McpServerTool(Name = "apply_multi_file_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "experimental", false, true,
        "Apply direct text edits to multiple files; optional verify + auto-revert on new compile errors."),
     Description("Apply text edits to multiple files in the workspace sequentially. All edits share a single pre-apply snapshot — revert_last_apply rolls back the entire batch atomically. If a file validation or apply fails partway through, revert_last_apply restores the pre-call state for any files that were already written. When verify=true, runs compile_check ONCE after the batch completes (scoped to the single owning project when the batch is single-project, or the full solution when it spans multiple) and attaches the new-error set as Verification. When autoRevertOnError=true AND new errors appeared, the whole batch is rolled back through the single batch-level undo snapshot. Prefer preview_multi_file_edit + apply_composite_preview for agent workflows that need a review step.")]
    public static Task<string> ApplyMultiFileEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of file edits. Each has filePath (string) and edits (array of TextEditDto with startLine, startColumn, endLine, endColumn, newText)")] FileEditsDto[] fileEdits,
        CancellationToken ct = default,
        [Description("When false (default), each C# file is parsed after edits; parser errors block that file's apply.")] bool skipSyntaxCheck = false,
        [Description("When true, run compile_check once after the batch completes (scoped to the single owning project when possible) and attach the result under Verification. Pre-existing errors are filtered out, so only NEW errors appear in the outcome. Default false.")] bool verify = false,
        [Description("When true AND verify surfaces new compile errors, automatically revert the entire batch through the single-slot undo path this call populated. Ignored when verify is false. Default false.")] bool autoRevertOnError = false)
    {
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            // Validate ALL paths before snapshotting so a bad path does not leave a stale undo entry.
            foreach (var fileEdit in fileEdits)
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, fileEdit.FilePath, c).ConfigureAwait(false);
            }

            var dto = await editService.ApplyMultiFileTextEditsAsync(workspaceId, fileEdits, c, skipSyntaxCheck, verify, autoRevertOnError).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "preview_multi_file_edit", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "experimental", true, false,
        "Preview a multi-file edit batch; returns per-file diffs and a preview token."),
     Description("Preview applying text edits to multiple files. Simulates every file's edits against a single Roslyn Solution snapshot and returns per-file unified diffs plus a preview token. Redeem via preview_multi_file_edit_apply.")]
    public static Task<string> PreviewMultiFileEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of file edits. Each has filePath (string) and edits (array of TextEditDto with startLine, startColumn, endLine, endColumn, newText)")] FileEditsDto[] fileEdits,
        CancellationToken ct = default,
        [Description("When false (default), each C# file is parsed after edits; syntax errors surface as an error response.")] bool skipSyntaxCheck = false)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            foreach (var fileEdit in fileEdits)
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, fileEdit.FilePath, c).ConfigureAwait(false);
            }
            var dto = await editService.PreviewMultiFileTextEditsAsync(workspaceId, fileEdits, c, skipSyntaxCheck).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "preview_multi_file_edit_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "experimental", false, true,
        "Apply a previously previewed multi-file edit."),
     Description("Apply a previously previewed multi-file edit. Rejects stale tokens if the workspace has changed since preview.")]
    public static Task<string> ApplyMultiFilePreview(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by preview_multi_file_edit")] string previewToken,
        CancellationToken ct = default)
    {
        var wsId = previewStore.PeekWorkspaceId(previewToken)
            ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
        return gate.RunWriteAsync(wsId, async c =>
        {
            var result = await refactoringService.ApplyRefactoringAsync(previewToken, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
