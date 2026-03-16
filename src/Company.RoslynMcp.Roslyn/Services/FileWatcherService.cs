using System.Collections.Concurrent;
using Company.RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace Company.RoslynMcp.Roslyn.Services;

public sealed class FileWatcherService(ILogger<FileWatcherService> logger) : IFileWatcherService
{
    private readonly ConcurrentDictionary<string, WatcherEntry> _watchers = new(StringComparer.Ordinal);

    public void Watch(string workspaceId, string workspacePath)
    {
        Unwatch(workspaceId);

        var directory = Path.GetDirectoryName(workspacePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        var entry = new WatcherEntry(watcher);
        _watchers[workspaceId] = entry;

        watcher.Changed += (_, _) => entry.MarkStale();
        watcher.Created += (_, _) => entry.MarkStale();
        watcher.Deleted += (_, _) => entry.MarkStale();
        watcher.Renamed += (_, _) => entry.MarkStale();

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

    private sealed class WatcherEntry(FileSystemWatcher watcher) : IDisposable
    {
        private volatile bool _isStale;

        public bool IsStale => _isStale;

        public void MarkStale() => _isStale = true;

        public void ClearStale() => _isStale = false;

        public void Dispose() => watcher.Dispose();
    }
}
