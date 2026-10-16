namespace RoslynMcp.Core.Services;

/// <summary>
/// Thread-safe, TTL-bounded in-memory store keyed by opaque string tokens.
/// Entries expire after <paramref name="ttl"/> (default 5 minutes) and the store is capped at
/// <paramref name="maxEntries"/> entries; oldest entries are evicted when the limit is reached.
/// </summary>
/// <typeparam name="TEntry">
/// The entry type. Must implement <see cref="IBoundedStoreEntry"/> to provide
/// the workspace ID and creation timestamp needed for TTL and eviction.
/// </typeparam>
public abstract class BoundedStore<TEntry> where TEntry : IBoundedStoreEntry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TEntry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    protected BoundedStore(int maxEntries = 20, TimeSpan ttl = default)
    {
        _maxEntries = maxEntries > 0 ? maxEntries : 20;
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);
    }

    protected string StoreEntry(TEntry entry)
    {
        lock (_gate)
        {
            CleanExpired();
            MakeRoomForEntry();
            var token = Guid.NewGuid().ToString("N");
            _entries[token] = entry;
            return token;
        }
    }

    protected TEntry? RetrieveEntry(string token)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(token, out var entry))
                return default;

            if (DateTime.UtcNow - entry.CreatedAt > _ttl)
            {
                _entries.Remove(token);
                return default;
            }

            return entry;
        }
    }

    public void Invalidate(string token)
    {
        lock (_gate)
        {
            _entries.Remove(token);
        }
    }

    public void InvalidateAll(string? workspaceId = null)
    {
        lock (_gate)
        {
            if (workspaceId is null)
            {
                _entries.Clear();
                return;
            }

            foreach (var kvp in _entries.ToArray())
            {
                if (string.Equals(kvp.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
                    _entries.Remove(kvp.Key);
            }
        }
    }

    /// <summary>
    /// Drops every entry for which <paramref name="shouldDrop"/> returns <see langword="true"/>.
    /// Hook for derived stores that need a richer eviction policy than workspace-id matching
    /// (e.g. <c>PreviewStore.InvalidateOnVersionBump</c>, which compares the post-bump
    /// workspace version against a per-entry pinned-range ceiling).
    /// </summary>
    protected void InvalidateWhere(Func<TEntry, bool> shouldDrop)
    {
        lock (_gate)
        {
            ArgumentNullException.ThrowIfNull(shouldDrop);
            foreach (var kvp in _entries.ToArray())
            {
                if (shouldDrop(kvp.Value))
                    _entries.Remove(kvp.Key);
            }
        }
    }

    public string? PeekWorkspaceId(string token)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(token, out var entry))
                return null;

            if (DateTime.UtcNow - entry.CreatedAt > _ttl)
            {
                _entries.Remove(token);
                return null;
            }

            return entry.WorkspaceId;
        }
    }

    private void CleanExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _entries.ToArray())
        {
            if (now - kvp.Value.CreatedAt > _ttl)
                _entries.Remove(kvp.Key);
        }
    }

    private void MakeRoomForEntry()
    {
        if (_entries.Count < _maxEntries) return;

        var toEvict = _entries
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_entries.Count - _maxEntries + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toEvict)
            _entries.Remove(key);
    }
}

public interface IBoundedStoreEntry
{
    string WorkspaceId { get; }
    DateTime CreatedAt { get; }
}
