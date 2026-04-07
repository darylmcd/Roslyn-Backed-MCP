using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<string, TEntry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    protected BoundedStore(int maxEntries = 20, TimeSpan ttl = default)
    {
        _maxEntries = maxEntries > 0 ? maxEntries : 20;
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);
    }

    protected string StoreEntry(TEntry entry)
    {
        CleanExpired();
        EvictIfOverLimit();
        var token = Guid.NewGuid().ToString("N");
        _entries[token] = entry;
        return token;
    }

    protected TEntry? RetrieveEntry(string token)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return default;

        if (DateTime.UtcNow - entry.CreatedAt > _ttl)
        {
            _entries.TryRemove(token, out _);
            return default;
        }

        return entry;
    }

    public void Invalidate(string token)
    {
        _entries.TryRemove(token, out _);
    }

    public void InvalidateAll(string? workspaceId = null)
    {
        if (workspaceId is null)
        {
            _entries.Clear();
            return;
        }

        foreach (var kvp in _entries)
        {
            if (string.Equals(kvp.Value.WorkspaceId, workspaceId, StringComparison.Ordinal))
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    public string? PeekWorkspaceId(string token)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return null;

        if (DateTime.UtcNow - entry.CreatedAt > _ttl)
        {
            _entries.TryRemove(token, out _);
            return null;
        }

        return entry.WorkspaceId;
    }

    private void CleanExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _entries)
        {
            if (now - kvp.Value.CreatedAt > _ttl)
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    private void EvictIfOverLimit()
    {
        if (_entries.Count <= _maxEntries) return;

        var toEvict = _entries
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_entries.Count - _maxEntries + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toEvict)
            _entries.TryRemove(key, out _);
    }
}

public interface IBoundedStoreEntry
{
    string WorkspaceId { get; }
    DateTime CreatedAt { get; }
}
