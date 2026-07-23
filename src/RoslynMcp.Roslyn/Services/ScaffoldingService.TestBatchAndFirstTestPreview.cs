using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class ScaffoldingService
{
    /// <summary>
    /// Delegates <c>scaffold_test_batch_preview</c> to the <see cref="BatchTestScaffolder"/>
    /// collaborator. The <see cref="IScaffoldingService"/> facade contract and DI lifetime are
    /// unchanged.
    /// </summary>
    public Task<RefactoringPreviewDto> PreviewScaffoldTestBatchAsync(
        string workspaceId, ScaffoldTestBatchDto request, CancellationToken ct) =>
        _batchTestScaffolder.PreviewBatchAsync(workspaceId, request, ct);

    /// <summary>
    /// Delegates <c>scaffold_first_test_file_preview</c> to the <see cref="BatchTestScaffolder"/>
    /// collaborator. The <see cref="IScaffoldingService"/> facade contract and DI lifetime are
    /// unchanged.
    /// </summary>
    public Task<RefactoringPreviewDto> PreviewScaffoldFirstTestFileAsync(
        string workspaceId, ScaffoldFirstTestFileDto request, CancellationToken ct) =>
        _batchTestScaffolder.PreviewFirstTestFileAsync(workspaceId, request, ct);
}
