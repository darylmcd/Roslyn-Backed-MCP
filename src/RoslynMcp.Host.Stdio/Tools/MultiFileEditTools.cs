using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for direct multi-file text-edit apply/preview operations.
/// WS1 phase 1.6 — <c>ApplyMultiFilePreview</c> (pure dispatch) delegates to
/// <see cref="ToolDispatch.ApplyByTokenAsync{TDto}(IWorkspaceExecutionGate, IPreviewStore, string, Func{CancellationToken, Task{TDto}}, CancellationToken)"/>;
/// <c>ApplyMultiFileEdit</c> and <c>PreviewMultiFileEdit</c> keep their hand-written
/// bodies because they perform per-file async path validation inside the gate
/// before the service call (validation must run under the gate's cancellation
/// token, so the single-lambda dispatch shape doesn't fit). See
/// <c>CodeActionTools</c> (canary, PR #305) and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c>.
/// </summary>
[McpServerToolType]
public static class MultiFileEditTools
{

    /// <remarks>
    /// If a file validation or apply fails partway through, <c>revert_last_apply</c> restores the
    /// pre-call state for any files already written. When <c>verify</c> is true, <c>compile_check</c>
    /// runs ONCE after the batch completes — scoped to the single owning project when the batch is
    /// single-project, or to the full solution when it spans several — and the new-error set is
    /// attached as Verification (pre-existing errors are filtered out). When
    /// <c>autoRevertOnError</c> is true AND new errors appeared, the whole batch is rolled back
    /// through that single batch-level undo snapshot.
    /// </remarks>
    [McpServerTool(Name = "apply_multi_file_edit", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "experimental", false, true,
        "Apply direct text edits to multiple files; optional verify + auto-revert on new compile errors."),
     Description("Apply text edits to multiple files under one atomic pre-apply snapshot; revert_last_apply rolls back the whole batch. Prefer preview_multi_file_edit + apply_composite_preview for review workflows.")]
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
            // preview-apply-token-write-path-toctou: the validator's contract requires callers that
            // go on to WRITE to persist to the boundary-canonicalized (fully link-resolved) path it
            // returns — re-walking the client-supplied string at write time re-resolves every
            // symlink/junction component, letting a link swapped between validation and write
            // redirect the bytes outside the boundary. Rewrite each FileEditsDto to the canonical
            // target instead of discarding the validator's return.
            var canonicalEdits = new FileEditsDto[fileEdits.Length];
            for (var i = 0; i < fileEdits.Length; i++)
            {
                var canonicalPath = await ClientRootPathValidator
                    .ValidatePathAgainstRootsAsync(server, fileEdits[i].FilePath, c).ConfigureAwait(false);
                canonicalEdits[i] = fileEdits[i] with { FilePath = canonicalPath };
            }

            var dto = await editService.ApplyMultiFileTextEditsAsync(workspaceId, canonicalEdits, "apply_multi_file_edit", c, skipSyntaxCheck, verify, autoRevertOnError).ConfigureAwait(false);
            return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "preview_multi_file_edit", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "experimental", true, false,
        "Preview a multi-file edit batch; returns per-file diffs and a preview token."),
     Description("Preview applying text edits to multiple files against a single Roslyn Solution snapshot. Returns per-file unified diffs plus a preview token. Redeem via preview_multi_file_edit_apply.")]
    public static Task<string> PreviewMultiFileEdit(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IEditService editService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Array of file edits. Each has filePath (string) and edits (array of TextEditDto with startLine, startColumn, endLine, endColumn, newText)")] FileEditsDto[] fileEdits,
        CancellationToken ct = default,
        [Description("When false (default), each C# file is parsed after edits; all non-hidden parse diagnostics and parser-recovered skipped text reject the preview. Set true only for intentional intermediate states.")] bool skipSyntaxCheck = false)
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
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "preview_multi_file_edit_apply", c),
            ct);
}
