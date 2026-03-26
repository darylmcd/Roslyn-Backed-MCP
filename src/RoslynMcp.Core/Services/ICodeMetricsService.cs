using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Calculates cyclomatic complexity, lines of code, nesting depth, and parameter count for methods in a workspace.
/// </summary>
public interface ICodeMetricsService
{
    Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct);
}
