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
/// settles.
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

    public void Watch(string workspaceId, string workspacePath)
    {
        Unwatch(workspaceId);

        var directory = Path.GetDirectoryName(workspacePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var entry = new WatcherEntry();
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

    private static void MarkStaleIfRelevant(WatcherEntry entry, string fullPath)
    {
        if (ShouldIgnorePath(fullPath))
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

    private static bool ShouldIgnorePath(string fullPath)
    {
        return fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WatcherEntry : IDisposable
    {
        private volatile bool _isStale;
        private string? _staleReason;
        private readonly object _reasonLock = new();
        private readonly List<FileSystemWatcher> _watchers = [];

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
            }
        }

        public void ClearStale()
        {
            lock (_reasonLock)
            {
                _isStale = false;
                _staleReason = null;
            }
        }

        public void Dispose()
        {
            foreach (var w in _watchers)
                w.Dispose();
        }
    }
}
