using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface ICodePatternAnalyzer
{
    Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
    Task<IReadOnlyList<SemanticSearchResultDto>> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct);
}
