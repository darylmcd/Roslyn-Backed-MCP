using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds clusters of near-duplicate method bodies across a workspace by AST-normalized
/// body hashing. Complements <see cref="IUnusedCodeAnalyzer"/> for discovery of
/// copy-paste that should be refactored into a shared helper.
/// </summary>
public interface IDuplicateMethodDetectorService
{
    Task<IReadOnlyList<DuplicatedMethodGroupDto>> FindDuplicatedMethodsAsync(
        string workspaceId,
        DuplicateMethodAnalysisOptions options,
        CancellationToken ct);
}
