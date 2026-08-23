using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Measures type cohesion via LCOM4 metrics and identifies shared private members
/// to inform SRP analysis and type extraction decisions.
/// </summary>
public interface ICohesionAnalysisService
{
    /// <summary>
    /// Computes LCOM4 cohesion metrics for types in the workspace, identifying
    /// independent method clusters that share no state.
    /// </summary>
    Task<IReadOnlyList<CohesionMetricsDto>> GetCohesionMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minMethods, int limit, bool includeInterfaces, bool excludeTestProjects, CancellationToken ct);

    /// <summary>
    /// Computes cohesion metrics while preserving whether every visited type was analyzed.
    /// Implementations predating the detailed contract retain source compatibility and project
    /// their legacy result as complete; the built-in service overrides this with measured
    /// per-type failure accounting.
    /// </summary>
    async Task<CohesionMetricsScanResult> GetCohesionMetricsDetailedAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minMethods, int limit, bool includeInterfaces, bool excludeTestProjects, CancellationToken ct) =>
        new(
            await GetCohesionMetricsAsync(
                workspaceId, filePath, projectFilter, minMethods, limit, includeInterfaces,
                excludeTestProjects, ct).ConfigureAwait(false),
            IsComplete: true,
            FailedTypeCount: 0);

    /// <summary>
    /// Finds private members of a type that are referenced by multiple public methods.
    /// Helps plan type extractions by identifying shared dependencies.
    /// </summary>
    Task<IReadOnlyList<SharedMemberDto>> FindSharedMembersAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct);
}
