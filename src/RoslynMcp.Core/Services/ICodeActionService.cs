using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides access to Roslyn code fixes and refactoring actions at a source location.
/// </summary>
public interface ICodeActionService
{
    /// <summary>
    /// Returns the code actions (fixes and refactorings) available at the given source location,
    /// wrapped with a human-readable hint explaining why the list is empty when no actions apply.
    /// </summary>
    Task<CodeActionListDto> GetCodeActionsAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, CancellationToken ct);

    /// <summary>
    /// Computes and stores a preview for the code action at the given source location.
    /// </summary>
    Task<RefactoringPreviewDto> PreviewCodeActionAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int? endLine, int? endColumn, int actionIndex, CancellationToken ct);
}
