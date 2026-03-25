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
    /// <param name="ct">Cancellation token.</param>
    Task<CompletionResultDto> GetCompletionsAsync(
        string workspaceId, string filePath, int line, int column, CancellationToken ct);
}
