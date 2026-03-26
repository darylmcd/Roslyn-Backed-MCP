using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface ICodeMetricsService
{
    Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct);
}
