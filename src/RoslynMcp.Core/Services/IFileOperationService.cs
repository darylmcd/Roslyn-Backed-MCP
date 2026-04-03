using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for creating, deleting, and moving source files within a workspace.
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// Previews creating a new source file in the specified project.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The file creation parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewCreateFileAsync(string workspaceId, CreateFileDto request, CancellationToken ct);

    /// <summary>
    /// Previews deleting an existing source file from the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The deletion parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewDeleteFileAsync(string workspaceId, DeleteFileDto request, CancellationToken ct);

    /// <summary>
    /// Previews moving a source file, optionally updating its namespace to match the destination.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The move parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewMoveFileAsync(string workspaceId, MoveFileDto request, CancellationToken ct);
}
