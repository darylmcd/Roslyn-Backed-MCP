using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

/// <summary>
/// Provides advanced heuristic analysis of a loaded workspace, including unused symbol detection,
/// dependency analysis, complexity metrics, and semantic search.
/// </summary>
public interface IAdvancedAnalysisService
{
    /// <summary>
    /// Finds symbols that appear to have no references within the loaded workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="includePublic">When <see langword="true"/>, includes public symbols that may nevertheless be unused.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId, string? projectFilter, bool includePublic, int limit, CancellationToken ct);

    /// <summary>
    /// Returns dependency injection service registrations discovered in source code.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    /// <summary>
    /// Computes cyclomatic complexity and related metrics for methods in the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">Restricts analysis to a specific file, or <see langword="null"/> for the whole workspace.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="minComplexity">Returns only methods at or above this complexity threshold, or <see langword="null"/> for no threshold.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct);

    /// <summary>
    /// Finds call sites that use reflection APIs (e.g., <c>Type.GetMethod</c>, <c>Activator.CreateInstance</c>).
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    /// <summary>
    /// Builds a namespace dependency graph, including detection of circular dependencies.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    /// <summary>
    /// Returns NuGet package references and version information for the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct);

    /// <summary>
    /// Searches for symbols whose names, kinds, or signatures match the given query.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="query">The search query text.</param>
    /// <param name="projectFilter">Restricts analysis to the named project, or <see langword="null"/> for all projects.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<SemanticSearchResultDto>> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct);
}
