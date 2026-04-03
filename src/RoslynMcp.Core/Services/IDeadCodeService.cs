using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview operations for removing dead (unused) code symbols from the workspace.
/// </summary>
public interface IDeadCodeService
{
    /// <summary>
    /// Previews removing the specified dead code symbols from the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="request">The dead code symbols to remove, identified by their symbol handles.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewRemoveDeadCodeAsync(string workspaceId, DeadCodeRemovalDto request, CancellationToken ct);
}
