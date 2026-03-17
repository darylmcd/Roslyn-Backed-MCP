namespace Company.RoslynMcp.Core.Services;

public interface ICompositePreviewStore
{
    string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations);
    (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token);
    void Invalidate(string token);
    void InvalidateAll(string? workspaceId = null);
}

public sealed record CompositeFileMutation(
    string FilePath,
    string? UpdatedContent,
    bool DeleteFile = false);