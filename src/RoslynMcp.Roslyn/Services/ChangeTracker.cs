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
    private readonly ConcurrentDictionary<string, WorkspaceChanges> _changes = new();
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<ChangeTracker>? _logger;
    private int _globalSequence;

    public event Action<ChangeRecordedEventArgs>? ChangeRecorded;

    /// <summary>Test seam: runs in <see cref="RecordChange"/> after the workspace's ledger state is acquired and before its lock is taken, i.e. the window a workspace close (<see cref="Clear"/>) can land in.</summary>
    internal Action? AfterStateAcquired { get; set; }

    /// <summary>Test seam: number of workspaces that own a ledger. Production code must not read it.</summary>
    internal int TrackedWorkspaceCount => _changes.Count;

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
        IReadOnlyList<string> affectedFiles, string toolName, UndoCaptureId capture)
    {
        // A closed (or never-loaded) workspace owns no ledger and fires no ChangeRecorded: re-creating its
        // list after Clear leaked it and re-triggered the undo commit path. WorkspaceManager removes the
        // session BEFORE raising WorkspaceClosed (which runs Clear), so liveness is the closed signal.
        if (!_workspaceManager.ContainsWorkspace(workspaceId))
            return;

        int seq;
        while (true)
        {
            var state = _changes.GetOrAdd(workspaceId, static _ => new WorkspaceChanges());
            AfterStateAcquired?.Invoke();
            lock (state)
            {
                if (state.Cleared)
                    continue; // Clear detached this ledger after we fetched it: re-acquire (and re-check liveness).

                // Re-check under the ledger lock: the workspace may have closed between the pre-check and
                // GetOrAdd, so the state just created is an orphan Clear will never see. Remove exactly it.
                if (!_workspaceManager.ContainsWorkspace(workspaceId))
                {
                    _changes.TryRemove(new KeyValuePair<string, WorkspaceChanges>(workspaceId, state));
                    return;
                }

                seq = Interlocked.Increment(ref _globalSequence);
                state.Entries.Add(new WorkspaceChangeDto(seq, description, affectedFiles, toolName, DateTime.UtcNow));
                break;
            }
        }

        // revert-apply-by-sequence-number: fire ChangeRecorded so IUndoService can commit the pending
        // pre-apply snapshot owned by the supplied capture id under the sequence number we just assigned. Handler
        // exceptions are swallowed — a misbehaving subscriber must not break the apply path.
        var handler = ChangeRecorded;
        if (handler is null) return;
        var notification = new ChangeRecordedEventArgs(workspaceId, seq, description, affectedFiles, toolName, capture);
        foreach (Action<ChangeRecordedEventArgs> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(notification);
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
        if (!_changes.TryGetValue(workspaceId, out var state))
            return [];

        lock (state)
        {
            return state.Entries.ToList();
        }
    }

    public void Clear(string workspaceId)
    {
        if (_changes.TryRemove(workspaceId, out var state))
        {
            lock (state)
            {
                state.Cleared = true;
            }
        }
    }

    /// <summary>One workspace's ledger and its lock; <see cref="Cleared"/> is set by <see cref="Clear"/> under that lock so a racing record never appends to a detached ledger.</summary>
    private sealed class WorkspaceChanges
    {
        public List<WorkspaceChangeDto> Entries { get; } = new();
        public bool Cleared { get; set; }
    }
}
