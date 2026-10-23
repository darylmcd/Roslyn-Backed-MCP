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
    /// errors unrelated to the edit are filtered out via a pre-vs-post identity
    /// diff (id+file+line — see <c>DiagnosticIdentitySet</c>), so a repo that
    /// already fails to compile will not mis-attribute its errors to this call.
    /// </param>
    /// <param name="autoRevertOnError">
    /// When <c>true</c> AND <paramref name="verify"/> surfaces new compile errors,
    /// the edit is compensated through the exact undo operation owned by this call.
    /// Superseded operation authority refuses compensation rather than overwriting later changes. Ignored when <paramref name="verify"/> is <c>false</c>.
    /// </param>
    /// <remarks>
    /// <paramref name="toolName"/> carries the originating MCP tool name to
    /// <see cref="IChangeTracker.RecordChange"/> so <c>workspace_changes</c> reports the writer
    /// that actually ran (e.g. <c>add_pragma_suppression</c>, <c>pragma_scope_widen</c>,
    /// <c>apply_text_edit</c>) instead of collapsing every <see cref="IEditService"/> caller
    /// into a generic <c>apply_text_edit</c> bucket
    /// (<c>workspace-changes-log-missing-editorconfig-writers</c>).
    /// </remarks>
    /// <param name="canonicalWritePath">
    /// Optional boundary-canonicalized (fully link-resolved) target to persist the edited text to.
    /// When a caller has already validated <paramref name="filePath"/> against the sanctioned-root
    /// boundary, it should pass the canonical path the validator returned: the physical write then
    /// lands on that pinned target instead of re-walking <paramref name="filePath"/> at write time,
    /// which would re-resolve any symlink/junction component and let an attacker who swaps a link
    /// between validation and write redirect the bytes outside the boundary
    /// (<c>path-boundary-link-swap-toctou</c>). <c>null</c> (the default) keeps the historical
    /// behavior of writing to the workspace document's own path.
    /// </param>
    Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId,
        string filePath,
        IReadOnlyList<TextEditDto> edits,
        string toolName,
        CancellationToken ct,
        bool skipSyntaxCheck = false,
        bool verify = false,
        bool autoRevertOnError = false,
        string? canonicalWritePath = null);

    /// <summary>
    /// Validates and simulates the complete ordered batch before effects; repeated-file edits compose
    /// against their accumulated input. Persists the final physical mutation set as one transaction,
    /// then publishes one owned undo operation and one change entry.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="fileEdits">The per-file edit batches to apply, in order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipSyntaxCheck">When <c>false</c> (default), each <c>.cs</c> file is parsed after edits; parser errors reject the entire batch before any file is changed.</param>
    /// <param name="verify">
    /// When <c>true</c>, run <c>compile_check</c> ONCE after the full batch completes,
    /// scoped to the union of owning projects for the edited files. The new-error set
    /// is attached to <see cref="MultiFileEditResultDto.Verification"/>.
    /// </param>
    /// <param name="autoRevertOnError">
    /// When <c>true</c> AND <paramref name="verify"/> surfaces new compile errors,
    /// the entire batch is compensated through the exact owned undo operation prepared by this call.
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
    /// token that can later be redeemed by <c>preview_multi_file_edit_apply</c> to commit the batch
    /// atomically. No disk writes occur at preview time.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="fileEdits">The per-file edit batches to preview, in order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="skipSyntaxCheck">When <c>false</c> (default), each <c>.cs</c> file is parsed after edits; parser errors refuse the entire preview before a token is issued.</param>
    Task<RefactoringPreviewDto> PreviewMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct, bool skipSyntaxCheck = false);
}
