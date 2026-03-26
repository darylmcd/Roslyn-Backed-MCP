using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IUnusedCodeAnalyzer
{
    Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId, string? projectFilter, bool includePublic, int limit, CancellationToken ct);
}
