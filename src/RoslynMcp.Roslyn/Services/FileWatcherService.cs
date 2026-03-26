using System.Collections.Concurrent;
using RoslynMcp.Core.Services;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

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

        entry.MarkStale();
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
        private readonly List<FileSystemWatcher> _watchers = [];

        public bool IsStale => _isStale;

        public void AddWatcher(FileSystemWatcher watcher) => _watchers.Add(watcher);

        public void MarkStale() => _isStale = true;

        public void ClearStale() => _isStale = false;

        public void Dispose()
        {
            foreach (var w in _watchers)
                w.Dispose();
        }
    }
}
