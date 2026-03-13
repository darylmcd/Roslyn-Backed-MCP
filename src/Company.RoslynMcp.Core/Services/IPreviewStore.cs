using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public interface IPreviewStore
{
    string Store(Solution modifiedSolution, int workspaceVersion, string description);
    (Solution ModifiedSolution, string Description)? Retrieve(string token, int currentWorkspaceVersion);
    void Invalidate(string token);
    void InvalidateAll();
}
