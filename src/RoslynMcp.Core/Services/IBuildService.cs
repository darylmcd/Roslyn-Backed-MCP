using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface IBuildService
{
    Task<BuildResultDto> BuildWorkspaceAsync(string workspaceId, CancellationToken ct);
    Task<BuildResultDto> BuildProjectAsync(string workspaceId, string projectName, CancellationToken ct);
}
