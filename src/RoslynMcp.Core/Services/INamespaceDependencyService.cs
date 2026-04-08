using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Builds the namespace-dependency graph for a workspace and reports any circular
/// dependencies detected via depth-first search of the graph.
/// </summary>
public interface INamespaceDependencyService
{
    Task<NamespaceDependencyGraphDto> GetNamespaceDependenciesAsync(
        string workspaceId, string? projectFilter, CancellationToken ct);
}
