using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Builds a workspace or individual project and returns structured compiler diagnostics.
/// </summary>
public interface IBuildService
{
    Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct);
    Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct);
}
