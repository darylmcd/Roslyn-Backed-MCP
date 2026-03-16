using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface ICodeActionService
{
    Task<IReadOnlyList<CodeActionDto>> GetCodeActionsAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, CancellationToken ct);

    Task<RefactoringPreviewDto> PreviewCodeActionAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, int actionIndex, CancellationToken ct);
}
