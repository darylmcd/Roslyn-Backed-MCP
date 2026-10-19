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
    // Monotonic insertion order: CreatedAt can tie on a coarse clock, and Dictionary enumeration
    // order is not insertion order once slots are reused, so eviction needs an explicit tie-break.
    private readonly Dictionary<string, long> _sequence = new();
    private long _nextSequence;
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
            _sequence[token] = _nextSequence++;
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
                RemoveEntry(token);
                return default;
            }

            return entry;
        }
    }

    public void Invalidate(string token)
    {
        lock (_gate)
        {
            RemoveEntry(token);
        }
    }

    public void InvalidateAll(string? workspaceId = null)
    {
        lock (_gate)
        {
            if (workspaceId is null)
            {
                _entries.Clear();
                _sequence.Clear();
                return;
            }

            foreach (var kvp in _entries.ToArray())
            {
                if (string.Equals(kvp.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
                    RemoveEntry(kvp.Key);
            }
        }
    }

    /// <summary>
    /// Drops every entry for which <paramref name="shouldDrop"/> returns <see langword="true"/>.
    /// Hook for derived stores that need a richer eviction policy than workspace-id matching
    /// (e.g. <c>PreviewStore.InvalidateOnVersionBump</c>, which compares the post-bump
    /// workspace version against a per-entry pinned-range ceiling).
    /// </summary>
    /// <remarks>The predicate runs while the store lock is held; it must not call back into the store.</remarks>
    protected void InvalidateWhere(Func<TEntry, bool> shouldDrop)
    {
        lock (_gate)
        {
            ArgumentNullException.ThrowIfNull(shouldDrop);
            foreach (var kvp in _entries.ToArray())
            {
                if (shouldDrop(kvp.Value))
                    RemoveEntry(kvp.Key);
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
                RemoveEntry(token);
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
                RemoveEntry(kvp.Key);
        }
    }

    private void RemoveEntry(string token)
    {
        _entries.Remove(token);
        _sequence.Remove(token);
    }

    private void MakeRoomForEntry()
    {
        if (_entries.Count < _maxEntries) return;

        var toEvict = _entries
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .ThenBy(kvp => _sequence[kvp.Key])
            .Take(_entries.Count - _maxEntries + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toEvict)
            RemoveEntry(key);
    }
}

public interface IBoundedStoreEntry
{
    string WorkspaceId { get; }
    DateTime CreatedAt { get; }
}
