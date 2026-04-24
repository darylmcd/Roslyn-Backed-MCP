using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Applies direct text edits to source files in a loaded workspace.
/// </summary>
public interface IEditService
{
    /// <summary>
    /// Applies the given text edits to the specified file and persists the changes to disk.
    /// Captures a single pre-apply snapshot in the undo stack so the change is revertible
    /// via <c>revert_last_apply</c>.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file to edit.</param>
    /// <param name="edits">The text edits to apply. Edits are applied in document order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipSyntaxCheck">When <c>false</c> (default), <c>.cs</c> files are parsed after edits; parser errors block apply. Set <c>true</c> for intentional non-compilable intermediate states.</param>
    /// <param name="verify">
    /// When <c>true</c>, run <c>compile_check</c> scoped to the owning project of
    /// <paramref name="filePath"/> after the edit is persisted. The new-error set
    /// is attached to <see cref="TextEditResultDto.Verification"/>. Pre-existing
    /// errors unrelated to the edit are filtered out via a pre-vs-post fingerprint
    /// diff, so a repo that already fails to compile will not mis-attribute its
    /// errors to this call.
    /// </param>
    /// <param name="autoRevertOnError">
    /// When <c>true</c> AND <paramref name="verify"/> surfaces new compile errors,
    /// the edit is rolled back through the same single-slot <c>revert_last_apply</c>
    /// undo path that this call just populated. Single-shot per call: only the
    /// snapshot this call captured can be reverted — prior-turn edits are never
    /// touched. Ignored when <paramref name="verify"/> is <c>false</c>.
    /// </param>
    /// <remarks>
    /// <paramref name="toolName"/> carries the originating MCP tool name to
    /// <see cref="IChangeTracker.RecordChange"/> so <c>workspace_changes</c> reports the writer
    /// that actually ran (e.g. <c>add_pragma_suppression</c>, <c>pragma_scope_widen</c>,
    /// <c>apply_text_edit</c>) instead of collapsing every <see cref="IEditService"/> caller
    /// into a generic <c>apply_text_edit</c> bucket
    /// (<c>workspace-changes-log-missing-editorconfig-writers</c>).
    /// </remarks>
    Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId,
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        string toolName,
        CancellationToken ct,
        bool skipSyntaxCheck = false,
        bool verify = false,
        bool autoRevertOnError = false);

    /// <summary>
    /// Applies text edits to multiple files atomically from an undo perspective: a single
    /// pre-apply snapshot is captured before the first file's edits, so a subsequent
    /// <c>revert_last_apply</c> rolls back the entire batch.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="fileEdits">The per-file edit batches to apply, in order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipSyntaxCheck">When <c>false</c> (default), each <c>.cs</c> file is parsed after edits; parser errors block that file's apply.</param>
    /// <param name="verify">
    /// When <c>true</c>, run <c>compile_check</c> ONCE after the full batch completes,
    /// scoped to the union of owning projects for the edited files. The new-error set
    /// is attached to <see cref="MultiFileEditResultDto.Verification"/>.
    /// </param>
    /// <param name="autoRevertOnError">
    /// When <c>true</c> AND <paramref name="verify"/> surfaces new compile errors,
    /// the entire batch is rolled back via the single batch-level undo snapshot.
    /// Ignored when <paramref name="verify"/> is <c>false</c>.
    /// </param>
    Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId,
        IReadOnlyList<FileEditsDto> fileEdits,
        string toolName,
        CancellationToken ct,
        bool skipSyntaxCheck = false,
        bool verify = false,
        bool autoRevertOnError = false);

    /// <summary>
    /// Item 5: previews a multi-file edit batch. Edits are simulated in-memory against the
    /// current workspace snapshot, producing a per-file unified diff and a composite preview
    /// token that can later be redeemed by <c>apply_composite_preview</c> to commit the batch
    /// atomically. No disk writes occur at preview time.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="fileEdits">The per-file edit batches to preview, in order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipSyntaxCheck">When <c>false</c> (default), each <c>.cs</c> file is parsed after edits; parser errors surface as a syntax-error warning on the preview and the preview token is not issued.</param>
    Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false);
}
