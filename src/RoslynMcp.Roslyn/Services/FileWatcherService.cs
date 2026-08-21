using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// FileSystemWatcher-backed implementation of <see cref="IFileWatcherService"/>.
/// Flags a workspace as stale when a tracked <c>.cs</c>/<c>.csproj</c>/<c>.props</c>/
/// <c>.targets</c>/<c>.sln</c>/<c>.slnx</c> file changes, and records a reason so
/// <c>workspace_status</c> can distinguish server-generated writes (<c>apply</c> /
/// <c>restore</c>) from genuinely external edits (<c>external-edit</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Attribution rule</strong> (<c>workspace-stale-after-external-edit-feedback</c>):
/// watcher-driven marks always record <see cref="StaleReasons.ExternalEdit"/>. The server
/// signals its own apply / restore writes by calling <see cref="MarkStale"/> explicitly with
/// the appropriate reason, either before the on-disk commit (so the later watcher event
/// finds the reason already set) or after (overwriting the external-edit attribution the
/// watcher stamped). <em>Last-writer-wins</em> inside a single stale window
/// (<see cref="ClearStale"/> resets to a clean slate); two independent events do not
/// compose. Callers that need to refuse on genuine external drift
/// (<c>change_signature_preview</c> and friends) query <see cref="GetStaleReason"/>; server
/// apply paths that want to preserve their attribution mark after the on-disk commit
/// settles call <see cref="MarkStale"/> once the write lands, overwriting any
/// <c>external-edit</c> stamp the watcher may have set.
/// </para>
/// <para>
/// <strong>CPU cost</strong>: purely event-driven; no periodic scans. <c>FileSystemWatcher</c>
/// can miss rapid-fire batched edits (buffer overflow), but the dominant risk is <em>over</em>-
/// firing (e.g. during <c>dotnet restore</c>'s <c>obj/</c> churn). We filter out <c>obj/</c>,
/// <c>bin/</c>, and <c>.git/</c> at ingress so a restore does not churn the flag for every file
/// it touches; the flag is a single <see langword="volatile"/> read on the hot path.
/// </para>
/// </remarks>
public sealed class FileWatcherService(ILogger<FileWatcherService> logger) : IFileWatcherService
{
    private readonly ConcurrentDictionary<string, WatcherEntry> _watchers = new(StringComparer.Ordinal);

    public event Action<string>? WorkspaceRootMissing;

    public void Watch(string workspaceId, string workspacePath)
    {
        Unwatch(workspaceId);

        var directory = Path.GetDirectoryName(workspacePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var entry = new WatcherEntry(directory);
        _watchers[workspaceId] = entry;

        // Watch .cs source files
        var csWatcher = CreateWatcher(directory, "*.cs", entry);
        entry.AddWatcher(csWatcher);

        // Watch project/solution files so project-level changes also mark the workspace stale
        foreach (var filter in new[] { "*.csproj", "*.props", "*.targets", "*.sln", "*.slnx" })
        {
            var projWatcher = CreateWatcher(directory, filter, entry);
            entry.AddWatcher(projWatcher);
        }

        // A workspace is anchored to the exact solution/project path that was loaded, not just
        // its containing directory. Keep a non-recursive identity watcher so deleting or
        // renaming that file retires the session even when sibling files keep the directory alive.
        var workspaceFileName = Path.GetFileName(workspacePath);
        if (!string.IsNullOrWhiteSpace(workspaceFileName))
        {
            var workspaceFileWatcher = new FileSystemWatcher(directory)
            {
                Filter = workspaceFileName,
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName,
            };
            workspaceFileWatcher.Deleted += (_, _) => RaiseWorkspaceRootMissing(workspaceId);
            workspaceFileWatcher.Renamed += (_, _) => RaiseWorkspaceRootMissing(workspaceId);
            workspaceFileWatcher.EnableRaisingEvents = true;
            entry.AddWatcher(workspaceFileWatcher);
        }

        var parentDirectory = Path.GetDirectoryName(directory);
        var rootName = Path.GetFileName(directory);
        if (!string.IsNullOrWhiteSpace(parentDirectory) &&
            !string.IsNullOrWhiteSpace(rootName) &&
            Directory.Exists(parentDirectory))
        {
            var rootWatcher = new FileSystemWatcher(parentDirectory)
            {
                Filter = rootName,
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
            };
            rootWatcher.Deleted += (_, _) => RaiseWorkspaceRootMissing(workspaceId);
            rootWatcher.Renamed += (_, _) => RaiseWorkspaceRootMissing(workspaceId);
            rootWatcher.EnableRaisingEvents = true;
            entry.AddWatcher(rootWatcher);
        }

        logger.LogInformation("Started file watcher for workspace {WorkspaceId} at {Directory}", workspaceId, directory);
    }

    public void Unwatch(string workspaceId)
    {
        if (_watchers.TryRemove(workspaceId, out var entry))
        {
            entry.Dispose();
            logger.LogInformation("Stopped file watcher for workspace {WorkspaceId}", workspaceId);
        }
    }

    public bool IsStale(string workspaceId)
    {
        return _watchers.TryGetValue(workspaceId, out var entry) && entry.IsStale;
    }

    public Task WaitForStaleAsync(string workspaceId, CancellationToken ct)
    {
        // Unknown workspace: nothing will ever signal, so don't hand back a task that hangs
        // until cancellation. Mirror IsStale's "unknown == not stale" with an immediate return.
        if (!_watchers.TryGetValue(workspaceId, out var entry))
        {
            return Task.CompletedTask;
        }

        return entry.WaitForStaleAsync(ct);
    }

    public string? GetStaleReason(string workspaceId)
    {
        return _watchers.TryGetValue(workspaceId, out var entry) ? entry.StaleReason : null;
    }

    public void MarkStale(string workspaceId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("reason is required.", nameof(reason));
        }

        if (_watchers.TryGetValue(workspaceId, out var entry))
        {
            entry.MarkStaleWithReason(reason);
        }
    }

    public void ClearStale(string workspaceId)
    {
        if (_watchers.TryGetValue(workspaceId, out var entry))
        {
            entry.ClearStale();
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _watchers)
        {
            kvp.Value.Dispose();
        }
        _watchers.Clear();
    }

    private static FileSystemWatcher CreateWatcher(string directory, string filter, WatcherEntry entry)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = filter,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, args) => MarkStaleIfRelevant(entry, args.FullPath);
        watcher.Created += (_, args) => MarkStaleIfRelevant(entry, args.FullPath);
        watcher.Deleted += (_, args) => MarkStaleIfRelevant(entry, args.FullPath);
        watcher.Renamed += (_, args) => MarkStaleIfRelevant(entry, args.FullPath);

        return watcher;
    }

    private void RaiseWorkspaceRootMissing(string workspaceId)
    {
        var handlers = WorkspaceRootMissing;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(workspaceId);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Workspace-root-missing subscriber failed for workspace {WorkspaceId}",
                    workspaceId);
            }
        }
    }

    private static void MarkStaleIfRelevant(WatcherEntry entry, string fullPath)
    {
        if (ShouldIgnorePath(fullPath, entry.RootDirectory))
        {
            return;
        }

        // workspace-stale-after-external-edit-feedback: a watcher-driven mark always represents
        // a file-system change from outside the server's in-process apply channel. Callers that
        // want to attribute a change to an apply/restore MUST call MarkStale explicitly BEFORE
        // the write hits disk (see WorkspaceManager.TryApplyChanges). External edits take
        // precedence: once set, a subsequent explicit MarkStale("apply") does not downgrade.
        entry.MarkStaleWithReason(StaleReasons.ExternalEdit);
    }

    private static bool ShouldIgnorePath(string fullPath, string rootDirectory)
    {
        return fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               (!ContainsPathSegment(rootDirectory, ".roslynmcp") &&
                fullPath.Contains($"{Path.DirectorySeparatorChar}.roslynmcp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPathSegment(string path, string segment)
    {
        return path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class WatcherEntry(string rootDirectory) : IDisposable
    {
        private volatile bool _isStale;
        private string? _staleReason;
        private readonly object _reasonLock = new();
        // Intentionally unguarded: AddWatcher is called only from FileWatcherService.Watch() on a
        // single thread during entry construction, and the FileSystemWatcher event callbacks
        // (Changed/Created/Deleted/Renamed -> MarkStaleIfRelevant) only ever touch the
        // _reasonLock-guarded reason state below — never this list — so _watchers has no concurrent
        // mutation today even though watchers may fire mid-construction. If a concurrent AddWatcher
        // path is ever introduced, guard this list consistently — e.g. under _reasonLock or a
        // dedicated lock — to match the synchronization model used for the rest of the mutable state.
        private readonly List<FileSystemWatcher> _watchers = [];

        // Completed when the entry flips stale; reset on ClearStale so a reloaded workspace can be
        // awaited again. RunContinuationsAsynchronously keeps the watcher callback that sets the
        // flag from running awaiter continuations inline on the FileSystemWatcher dispatch thread.
        private TaskCompletionSource _staleSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string RootDirectory { get; } = rootDirectory;

        public bool IsStale => _isStale;

        public string? StaleReason
        {
            get
            {
                // Volatile read of _isStale gates the reason: if IsStale=false we report null
                // even if a stale reason was previously set and not yet cleared in this memory
                // fence, keeping the (IsStale, StaleReason) pair internally consistent.
                if (!_isStale) return null;
                lock (_reasonLock)
                {
                    return _staleReason;
                }
            }
        }

        public void AddWatcher(FileSystemWatcher watcher) => _watchers.Add(watcher);

        /// <summary>
        /// Returns a task that completes once the entry is (or becomes) stale, honoring
        /// <paramref name="ct"/>. Lets callers await the real staleness signal instead of polling.
        /// </summary>
        public Task WaitForStaleAsync(CancellationToken ct)
        {
            Task signal;
            lock (_reasonLock)
            {
                if (_isStale)
                {
                    return Task.CompletedTask;
                }

                signal = _staleSignal.Task;
            }

            return ct.CanBeCanceled ? signal.WaitAsync(ct) : signal;
        }

        /// <summary>
        /// Marks the entry stale and records <paramref name="reason"/>. Last-writer-wins
        /// inside a single stale window: each call overwrites the prior reason until
        /// <see cref="ClearStale"/> resets the slate.
        /// </summary>
        public void MarkStaleWithReason(string reason)
        {
            lock (_reasonLock)
            {
                _staleReason = reason;
                _isStale = true;
                // Release any awaiters parked on WaitForStaleAsync. TrySetResult is a no-op when
                // the signal already fired earlier in this (not-yet-cleared) stale window.
                _staleSignal.TrySetResult();
            }
        }

        public void ClearStale()
        {
            lock (_reasonLock)
            {
                _isStale = false;
                _staleReason = null;
                // Cancel the outgoing signal BEFORE replacing it. An awaiter parked on
                // WaitForStaleAsync holds a reference to this exact TCS's Task; swapping in a fresh
                // one without resolving the old leaves that awaiter stranded until ITS OWN
                // CancellationToken deadline. A clear/re-arm is the abandonment of the pending
                // stale-wait (the entry just went clean, the opposite of stale), so cancellation —
                // not completion — is the correct signal: completing would falsely wake the awaiter
                // as "stale" when IsStale is now false. TrySetCanceled is a no-op when the TCS
                // already fired earlier in this stale window, and races safely with a concurrent
                // MarkStaleWithReason completer (both use the Try* form under _reasonLock).
                _staleSignal.TrySetCanceled();
                // Arm a fresh signal so a post-reload write can be awaited again.
                _staleSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Dispose()
        {
            foreach (var w in _watchers)
                w.Dispose();
        }
    }
}
