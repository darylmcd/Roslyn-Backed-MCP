using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IRefactoringService
{
    Task<RefactoringPreviewDto> PreviewRenameAsync(string filePath, int line, int column, string newName, CancellationToken ct);
    Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string filePath, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string filePath, CancellationToken ct);
}
