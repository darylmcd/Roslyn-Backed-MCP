using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Performs semantic search across a workspace using natural language queries and detects
/// reflection API usage patterns.
/// </summary>
public interface ICodePatternAnalyzer
{
    Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    async Task<ReflectionUsageScanResult> FindReflectionUsagesDetailedAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        var usages = await FindReflectionUsagesAsync(workspaceId, projectFilter, ct).ConfigureAwait(false);
        return new ReflectionUsageScanResult(usages, IsComplete: true, FailedDocumentCount: 0);
    }

    Task<SemanticSearchResponseDto> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct);
}
