using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositePreviewStore : BoundedStore<CompositePreviewStore.Entry>, ICompositePreviewStore
{
    private readonly PersistentCompositeStorage? _diskBackend;

    public CompositePreviewStore(int maxEntries = 20, TimeSpan ttl = default) : base(maxEntries, ttl)
    {
        _diskBackend = null;
    }

    /// <summary>
    /// Item 6 (v1.18): construct with disk persistence. Activated by the host when
    /// <see cref="PreviewStoreOptions.PersistDirectory"/> is set so cross-process clients can
    /// redeem tokens.
    /// </summary>
    public CompositePreviewStore(int maxEntries, TimeSpan ttl, PersistentCompositeStorage? diskBackend) : base(maxEntries, ttl)
    {
        _diskBackend = diskBackend;
    }

    public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
    {
        var entry = new Entry(workspaceId, workspaceVersion, description, mutations, DateTime.UtcNow);
        var token = StoreEntry(entry);
        // Write-through to disk so a separate process can redeem this token immediately.
        _diskBackend?.Write(token, entry);
        return token;
    }

    public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token)
    {
        Entry? entry;
        if (_diskBackend is not null)
        {
            // The persistent record is the shared one-time authority. Never fall back to the
            // creator process's in-memory copy after another host wins the disk claim.
            entry = _diskBackend.TryClaim(token);
            if (entry is not null)
            {
                base.Invalidate(token);
            }
        }
        else
        {
            entry = RetrieveEntry(token);
        }

        if (entry is null) return null;
        return (entry.WorkspaceId, entry.WorkspaceVersion, entry.Description, entry.Mutations);
    }

    public new void Invalidate(string token)
    {
        base.Invalidate(token);
        _diskBackend?.Delete(token);
    }

    public sealed record Entry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<CompositeFileMutation> Mutations,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
