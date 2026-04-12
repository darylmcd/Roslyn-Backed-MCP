using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Records all mutations applied to workspaces during a session.
/// Thread-safe. Clears per-workspace data on workspace close.
/// </summary>
public sealed class ChangeTracker : IChangeTracker
{
    private readonly ConcurrentDictionary<string, List<WorkspaceChangeDto>> _changes = new();
    private int _globalSequence;

    public void RecordChange(string workspaceId, string description,
        IReadOnlyList<string> affectedFiles, string toolName)
    {
        var seq = Interlocked.Increment(ref _globalSequence);
        var entry = new WorkspaceChangeDto(seq, description, affectedFiles, toolName, DateTime.UtcNow);

        _changes.AddOrUpdate(
            workspaceId,
            _ => [entry],
            (_, list) => { lock (list) { list.Add(entry); } return list; });
    }

    public IReadOnlyList<WorkspaceChangeDto> GetChanges(string workspaceId)
    {
        if (!_changes.TryGetValue(workspaceId, out var list))
            return [];

        lock (list)
        {
            return list.ToList();
        }
    }

    public void Clear(string workspaceId)
    {
        _changes.TryRemove(workspaceId, out _);
    }
}
