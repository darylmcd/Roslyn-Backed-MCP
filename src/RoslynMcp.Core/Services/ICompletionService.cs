using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides IntelliSense-style completion items at a source position.
/// </summary>
public interface ICompletionService
{
    /// <summary>
    /// Returns completion candidates for the given position in a source file.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="line">The 1-based line number of the cursor position.</param>
    /// <param name="column">The 1-based column number of the cursor position.</param>
    /// <param name="filterText">
    /// Optional case-insensitive prefix filter that narrows the result to completion items whose
    /// <c>FilterText</c> or <c>DisplayText</c> starts with the supplied value (UX-007). When
    /// <see langword="null"/> or empty, no filter is applied.
    /// </param>
    /// <param name="maxItems">
    /// Maximum number of completion items to return. Defaults to 100, must be greater than zero (UX-007).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<CompletionResultDto> GetCompletionsAsync(
        string workspaceId,
        string filePath,
        int line,
        int column,
        string? filterText,
        int maxItems,
        CancellationToken ct);
}
