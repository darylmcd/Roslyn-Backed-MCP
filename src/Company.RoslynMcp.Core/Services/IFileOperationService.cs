using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IFileOperationService
{
    Task<RefactoringPreviewDto> PreviewCreateFileAsync(string workspaceId, CreateFileDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewDeleteFileAsync(string workspaceId, DeleteFileDto request, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewMoveFileAsync(string workspaceId, MoveFileDto request, CancellationToken ct);
}