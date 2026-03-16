using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Core.Services;

public interface IAdvancedAnalysisService
{
    Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        string workspaceId, string? projectFilter, bool includePublic, int limit, CancellationToken ct);

    Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct);

    Task<IReadOnlyList<ReflectionUsageDto>> FindReflectionUsagesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);

    Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct);

    Task<IReadOnlyList<SemanticSearchResultDto>> SemanticSearchAsync(
        string workspaceId, string query, string? projectFilter, int limit, CancellationToken ct);
}
