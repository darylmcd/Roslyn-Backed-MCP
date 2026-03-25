using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Applies direct text edits to source files in a loaded workspace.
/// </summary>
public interface IEditService
{
    /// <summary>
    /// Applies the given text edits to the specified file and persists the changes to disk.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the file to edit.</param>
    /// <param name="edits">The text edits to apply. Edits are applied in document order.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TextEditResultDto> ApplyTextEditsAsync(
        string workspaceId, string filePath, IReadOnlyList<TextEditDto> edits, CancellationToken ct);
}
