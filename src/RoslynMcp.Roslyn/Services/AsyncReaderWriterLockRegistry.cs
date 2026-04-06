using System.Collections.Concurrent;
using Nito.AsyncEx;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Per-workspace registry of <see cref="AsyncReaderWriterLock"/> instances. Used by
/// <see cref="WorkspaceExecutionGate"/> when the reader/writer lock feature flag is enabled.
/// </summary>
/// <remarks>
/// <see cref="AsyncReaderWriterLock"/> is FIFO-fair: a writer queued behind one or more readers
/// is granted before any subsequent reader. This avoids writer starvation under sustained read
/// load without an explicit writer-preference policy.
///
/// <para>
/// Locks are not <see cref="IDisposable"/>; <see cref="Remove"/> only drops the dictionary entry.
/// Any handle already held by an in-flight caller continues to function. The caller is
/// responsible for ensuring no in-flight reader/writer is operating against a workspace before
/// removing it (see <see cref="IWorkspaceExecutionGate.RemoveGate"/>).
/// </para>
/// </remarks>
internal sealed class AsyncReaderWriterLockRegistry
{
    private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Get or create the lock for the given workspace id.
    /// </summary>
    public AsyncReaderWriterLock Get(string workspaceId)
    {
        return _locks.GetOrAdd(workspaceId, _ => new AsyncReaderWriterLock());
    }

    /// <summary>
    /// Remove the lock entry for the given workspace id from the registry. Any handle already
    /// held by an in-flight caller continues to function.
    /// </summary>
    public void Remove(string workspaceId)
    {
        _locks.TryRemove(workspaceId, out _);
    }
}
