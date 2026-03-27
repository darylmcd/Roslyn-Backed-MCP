using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositePreviewStore : BoundedStore<CompositePreviewStore.Entry>, ICompositePreviewStore
{
    public CompositePreviewStore(int maxEntries = 20) : base(maxEntries) { }

    public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
    {
        return StoreEntry(new Entry(workspaceId, workspaceVersion, description, mutations, DateTime.UtcNow));
    }

    public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token)
    {
        var entry = RetrieveEntry(token);
        if (entry is null) return null;
        return (entry.WorkspaceId, entry.WorkspaceVersion, entry.Description, entry.Mutations);
    }

    public sealed record Entry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<CompositeFileMutation> Mutations,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
