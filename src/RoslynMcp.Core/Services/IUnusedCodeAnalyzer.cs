using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds symbols with zero references across a solution to identify dead code candidates.
/// </summary>
public interface IUnusedCodeAnalyzer
{
    Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId,
        UnusedSymbolsAnalysisOptions options,
        CancellationToken ct);
}
