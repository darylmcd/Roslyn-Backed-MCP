using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Calculates cyclomatic complexity, lines of code, nesting depth, and parameter count for methods in a workspace.
/// </summary>
public interface ICodeMetricsService
{
    /// <summary>
    /// Computes complexity metrics for methods in the workspace. When both <paramref name="filePath"/>
    /// and <paramref name="filePaths"/> are supplied, the union is taken (logical OR). Empty or null
    /// <paramref name="filePaths"/> means "no additional filter" (same as null).
    /// </summary>
    Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, IReadOnlyList<string>? filePaths, string? projectFilter, int? minComplexity, int limit, CancellationToken ct);
}
