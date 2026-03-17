namespace Company.RoslynMcp.Core.Services;

public interface IProjectMutationPreviewStore
{
    string Store(string workspaceId, string projectFilePath, string updatedContent, int workspaceVersion, string description);
    (string WorkspaceId, string ProjectFilePath, string UpdatedContent, int WorkspaceVersion, string Description)? Retrieve(string token);
    void Invalidate(string token);
    void InvalidateAll(string? workspaceId = null);
}