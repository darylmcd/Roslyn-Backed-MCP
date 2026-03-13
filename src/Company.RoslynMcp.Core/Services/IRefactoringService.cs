using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IRefactoringService
{
    Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct);
    Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct);
    Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct);
}
