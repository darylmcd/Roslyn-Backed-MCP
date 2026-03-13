using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public interface IPreviewStore
{
    string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description);
    (string WorkspaceId, Solution ModifiedSolution, int WorkspaceVersion, string Description)? Retrieve(string token);
    void Invalidate(string token);
    void InvalidateAll(string? workspaceId = null);
}
