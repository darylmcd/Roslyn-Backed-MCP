using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Records all mutations applied to workspaces during a session.
/// Thread-safe. Clears per-workspace data on workspace close.
/// </summary>
public sealed class ChangeTracker : IChangeTracker, IDisposable
{
    private readonly ConcurrentDictionary<string, List<WorkspaceChangeDto>> _changes = new();
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<ChangeTracker>? _logger;
    private int _globalSequence;

    public event Action<ChangeRecordedEventArgs>? ChangeRecorded;

    public ChangeTracker(IWorkspaceManager workspaceManager, ILogger<ChangeTracker>? logger = null)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
        _workspaceManager.WorkspaceClosed += Clear;
    }

    public void Dispose()
    {
        _workspaceManager.WorkspaceClosed -= Clear;
    }

    public void RecordChange(string workspaceId, string description,
        IReadOnlyList<string> affectedFiles, string toolName)
    {
        var seq = Interlocked.Increment(ref _globalSequence);
        var entry = new WorkspaceChangeDto(seq, description, affectedFiles, toolName, DateTime.UtcNow);

        _changes.AddOrUpdate(
            workspaceId,
            _ => [entry],
            (_, list) => { lock (list) { list.Add(entry); } return list; });

        // revert-apply-by-sequence-number: fire ChangeRecorded so IUndoService can commit its
        // pending pre-apply snapshot under the same sequence number we just assigned. Handler
        // exceptions are swallowed — a misbehaving subscriber must not break the apply path.
        var handler = ChangeRecorded;
        if (handler is not null)
        {
            try
            {
                handler(new ChangeRecordedEventArgs(workspaceId, seq, description, affectedFiles, toolName));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "ChangeTracker.ChangeRecorded handler threw for workspace {WorkspaceId} sequence {Sequence}; suppressing.",
                    workspaceId,
                    seq);
            }
        }
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
