using Company.RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public interface IWorkspaceManager
{
    Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct);
    Task<WorkspaceStatusDto> ReloadAsync(CancellationToken ct);
    WorkspaceStatusDto GetStatus();
    int CurrentVersion { get; }
    bool IsLoaded { get; }
    Solution GetCurrentSolution();
    bool TryApplyChanges(Solution newSolution);
    void IncrementVersion();
}
