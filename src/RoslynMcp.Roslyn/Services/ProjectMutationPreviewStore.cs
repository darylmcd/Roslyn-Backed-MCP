using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class ProjectMutationPreviewStore : BoundedStore<ProjectMutationPreviewStore.Entry>, IProjectMutationPreviewStore
{
    public ProjectMutationPreviewStore(int maxEntries = 20) : base(maxEntries) { }

    public string Store(string workspaceId, string projectFilePath, string updatedContent, int workspaceVersion, string description)
    {
        return StoreEntry(new Entry(workspaceId, projectFilePath, updatedContent, workspaceVersion, description, DateTime.UtcNow));
    }

    public (string WorkspaceId, string ProjectFilePath, string UpdatedContent, int WorkspaceVersion, string Description)? Retrieve(string token)
    {
        var entry = RetrieveEntry(token);
        if (entry is null) return null;
        return (entry.WorkspaceId, entry.ProjectFilePath, entry.UpdatedContent, entry.WorkspaceVersion, entry.Description);
    }

    public sealed record Entry(
        string WorkspaceId,
        string ProjectFilePath,
        string UpdatedContent,
        int WorkspaceVersion,
        string Description,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
