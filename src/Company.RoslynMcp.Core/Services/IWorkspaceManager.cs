using Company.RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public interface IWorkspaceManager
{
    Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct);
    Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct);
    WorkspaceStatusDto GetStatus(string workspaceId);
    int GetCurrentVersion(string workspaceId);
    Solution GetCurrentSolution(string workspaceId);
    bool TryApplyChanges(string workspaceId, Solution newSolution);
}
