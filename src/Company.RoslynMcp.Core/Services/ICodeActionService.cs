using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

/// <summary>
/// Provides access to Roslyn code fixes and refactoring actions at a source location.
/// </summary>
public interface ICodeActionService
{
    /// <summary>
    /// Returns the code actions (fixes and refactorings) available at the given source location.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="startLine">The 1-based start line of the selection range.</param>
    /// <param name="startColumn">The 1-based start column of the selection range.</param>
    /// <param name="endLine">The 1-based end line of the selection range, or <see langword="null"/> for a single-position request.</param>
    /// <param name="endColumn">The 1-based end column of the selection range, or <see langword="null"/> for a single-position request.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CodeActionDto>> GetCodeActionsAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, CancellationToken ct);

    /// <summary>
    /// Computes and stores a preview for the code action at the given source location.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="startLine">The 1-based start line of the selection range.</param>
    /// <param name="startColumn">The 1-based start column of the selection range.</param>
    /// <param name="endLine">The 1-based end line of the selection range, or <see langword="null"/> for a single-position request.</param>
    /// <param name="endColumn">The 1-based end column of the selection range, or <see langword="null"/> for a single-position request.</param>
    /// <param name="actionIndex">The zero-based index into the list returned by <see cref="GetCodeActionsAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewCodeActionAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, int actionIndex, CancellationToken ct);
}
