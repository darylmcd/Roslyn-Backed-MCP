using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Computes per-type afferent (Ca) / efferent (Ce) coupling plus Martin's instability index (I).
/// Companion to <see cref="ICohesionAnalysisService"/> — cohesion looks inward (how tightly a type
/// holds together), coupling looks outward (how the type relates to the rest of the codebase).
/// </summary>
public interface ICouplingAnalysisService
{
    /// <summary>
    /// Computes coupling metrics for types in the workspace.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier from <c>workspace_load</c>.</param>
    /// <param name="projectFilter">Optional project name filter (case-insensitive exact match).</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="excludeTestProjects">When true, skip MSBuild test projects from analysis.</param>
    /// <param name="includeInterfaces">When true, include interfaces alongside classes/structs/records.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CouplingMetricsDto>> GetCouplingMetricsAsync(
        string workspaceId,
        string? projectFilter,
        int limit,
        bool excludeTestProjects,
        bool includeInterfaces,
        CancellationToken ct);
}
