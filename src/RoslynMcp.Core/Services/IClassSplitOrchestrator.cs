using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Orchestrates splitting a type into a new partial class file.
/// </summary>
public interface IClassSplitOrchestrator
{
    Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct);
}
