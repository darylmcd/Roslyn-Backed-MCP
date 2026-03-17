using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IDeadCodeService
{
    Task<RefactoringPreviewDto> PreviewRemoveDeadCodeAsync(string workspaceId, DeadCodeRemovalDto request, CancellationToken ct);
}