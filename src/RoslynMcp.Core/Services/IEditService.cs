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
    Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct);

    /// <summary>
    /// Applies text edits to multiple files atomically from an undo perspective: a single
    /// pre-apply snapshot is captured before the first file's edits, so a subsequent
    /// <c>revert_last_apply</c> rolls back the entire batch.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="fileEdits">The per-file edit batches to apply, in order.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<MultiFileEditResultDto> ApplyMultiFileTextEditsAsync(
        string workspaceId, IReadOnlyList<FileEditsDto> fileEdits, CancellationToken ct);
}
