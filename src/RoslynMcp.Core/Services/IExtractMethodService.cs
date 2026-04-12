using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IExtractMethodService
{
    Task<RefactoringPreviewDto> PreviewExtractMethodAsync(
        string workspaceId, string filePath,
        int startLine, int startColumn, int endLine, int endColumn,
        string methodName, CancellationToken ct);
}
