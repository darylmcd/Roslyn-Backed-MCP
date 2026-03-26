using System.Collections.Concurrent;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositePreviewStore : ICompositePreviewStore
{
    private readonly ConcurrentDictionary<string, PreviewEntry> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);
    private readonly int _maxEntries;

    public CompositePreviewStore(int maxEntries = 20)
    {
        _maxEntries = maxEntries > 0 ? maxEntries : 20;
    }

    public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
    {
        CleanExpired();
        EvictIfOverLimit();
        var token = Guid.NewGuid().ToString("N");
        _entries[token] = new PreviewEntry(workspaceId, workspaceVersion, description, mutations, DateTime.UtcNow);
        return token;
    }

    public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(string token)
    {
        if (!_entries.TryGetValue(token, out var entry))
        {
            return null;
        }

        if (DateTime.UtcNow - entry.CreatedAt > _ttl)
        {
            _entries.TryRemove(token, out _);
            return null;
        }

        return (entry.WorkspaceId, entry.WorkspaceVersion, entry.Description, entry.Mutations);
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
            {
                _entries.TryRemove(kvp.Key, out _);
            }
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
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void EvictIfOverLimit()
    {
        if (_entries.Count <= _maxEntries)
        {
            return;
        }

        var toEvict = _entries.OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_entries.Count - _maxEntries + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toEvict)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private sealed record PreviewEntry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<CompositeFileMutation> Mutations,
        DateTime CreatedAt);
}