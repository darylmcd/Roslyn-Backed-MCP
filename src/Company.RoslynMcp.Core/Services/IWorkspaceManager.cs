using Company.RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public interface IWorkspaceManager
{
    Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct);
    Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct);
    bool Close(string workspaceId);
    IReadOnlyList<WorkspaceStatusDto> ListWorkspaces();
    WorkspaceStatusDto GetStatus(string workspaceId);
    ProjectGraphDto GetProjectGraph(string workspaceId);
    Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct);
    Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct);
    int GetCurrentVersion(string workspaceId);
    Solution GetCurrentSolution(string workspaceId);
    bool TryApplyChanges(string workspaceId, Solution newSolution);
}
