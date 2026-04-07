using Microsoft.CodeAnalysis;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Thread-safe, TTL-bounded in-memory store for pending Roslyn <see cref="Solution"/> previews.
/// Entries expire after a configurable TTL (default 5 minutes). The store is capped at
/// <paramref name="maxEntries"/> entries; oldest entries are evicted when the limit is reached.
/// </summary>
public sealed class PreviewStore : BoundedStore<PreviewStore.PreviewEntry>, IPreviewStore
{
    public PreviewStore(int maxEntries = 20, TimeSpan? ttl = null)
        : base(maxEntries, ttl ?? TimeSpan.FromMinutes(5)) { }

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
        => StoreEntry(new PreviewEntry(workspaceId, modifiedSolution, workspaceVersion, description, DateTime.UtcNow));

    public (string WorkspaceId, Solution ModifiedSolution, int WorkspaceVersion, string Description)? Retrieve(string token)
    {
        var entry = RetrieveEntry(token);
        if (entry is null) return null;
        return (entry.WorkspaceId, entry.ModifiedSolution, entry.WorkspaceVersion, entry.Description);
    }

    public sealed record PreviewEntry(
        string WorkspaceId,
        Solution ModifiedSolution,
        int WorkspaceVersion,
        string Description,
        DateTime CreatedAt) : IBoundedStoreEntry;
}
