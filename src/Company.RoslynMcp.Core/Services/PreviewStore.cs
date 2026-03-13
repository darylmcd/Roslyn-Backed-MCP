using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Company.RoslynMcp.Core.Services;

public sealed class PreviewStore : IPreviewStore
{
    private readonly ConcurrentDictionary<string, PreviewEntry> _entries = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);

    public string Store(Solution modifiedSolution, int workspaceVersion, string description)
    {
        CleanExpired();
        var token = Guid.NewGuid().ToString("N");
        _entries[token] = new PreviewEntry(modifiedSolution, workspaceVersion, description, DateTime.UtcNow);
        return token;
    }

    public (Solution ModifiedSolution, string Description)? Retrieve(string token, int currentWorkspaceVersion)
    {
        if (!_entries.TryGetValue(token, out var entry))
            return null;

        if (DateTime.UtcNow - entry.CreatedAt > _ttl)
        {
            _entries.TryRemove(token, out _);
            return null;
        }

        if (entry.WorkspaceVersion != currentWorkspaceVersion)
            return null;

        return (entry.ModifiedSolution, entry.Description);
    }

    public void Invalidate(string token)
    {
        _entries.TryRemove(token, out _);
    }

    public void InvalidateAll()
    {
        _entries.Clear();
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

    private sealed record PreviewEntry(
        Solution ModifiedSolution,
        int WorkspaceVersion,
        string Description,
        DateTime CreatedAt);
}
