using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Thread-safe, TTL-bounded in-memory store for pending Roslyn solution previews.
/// Entries expire after 5 minutes. The store is capped at 50 entries; oldest entries are
/// evicted when the limit is reached.
/// </summary>
public sealed class PreviewStore : IPreviewStore
{
    private readonly ConcurrentDictionary<string, PreviewEntry> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);

    private const int MaxEntries = 50;

    public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description)
    {
        CleanExpired();
        EvictIfOverLimit();
        var token = Guid.NewGuid().ToString("N");
        _entries[token] = new PreviewEntry(workspaceId, modifiedSolution, workspaceVersion, description, DateTime.UtcNow);
        return token;
    }

    public (string WorkspaceId, Solution ModifiedSolution, int WorkspaceVersion, string Description)? Retrieve(string token)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return null;

        if (DateTime.UtcNow - entry.CreatedAt > _ttl)
        {
            _entries.TryRemove(token, out _);
            return null;
        }

        return (entry.WorkspaceId, entry.ModifiedSolution, entry.WorkspaceVersion, entry.Description);
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
        if (_entries.Count <= MaxEntries) return;

        var toEvict = _entries
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_entries.Count - MaxEntries + 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toEvict)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private sealed record PreviewEntry(
        string WorkspaceId,
        Solution ModifiedSolution,
        int WorkspaceVersion,
        string Description,
        DateTime CreatedAt);
}
