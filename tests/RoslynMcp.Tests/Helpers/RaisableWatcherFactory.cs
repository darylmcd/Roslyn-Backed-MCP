namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Watcher-construction seam for <c>FileWatcherService</c> tests. Creates real
/// <see cref="FileSystemWatcher"/> subclasses that count disposals, can raise events synchronously
/// (no OS involvement), and can be made to fail on the Nth creation.
/// </summary>
internal sealed class RaisableWatcherFactory
{
    private readonly object _sync = new();
    private readonly List<RaisableWatcher> _created = [];
    private readonly Func<int, string, Exception?> _failure;
    private int _count;

    public RaisableWatcherFactory(Func<int, string, Exception?>? failure = null)
    {
        _failure = failure ?? ((_, _) => null);
    }

    public FileSystemWatcher Create(string directory)
    {
        int ordinal;
        lock (_sync)
            ordinal = ++_count;

        if (_failure(ordinal, directory) is { } failure)
            throw failure;

        var watcher = new RaisableWatcher(directory);
        lock (_sync)
            _created.Add(watcher);
        return watcher;
    }

    public IReadOnlyList<RaisableWatcher> Created
    {
        get
        {
            lock (_sync)
                return [.. _created];
        }
    }

    /// <summary>The recursive watcher the service puts on a workspace root.</summary>
    public RaisableWatcher Tree(string rootDirectory) =>
        Created.Single(w => w.IncludeSubdirectories && PathEquals(w.Path, rootDirectory));

    /// <summary>The non-recursive shared watcher the service puts on an ancestor directory.</summary>
    public RaisableWatcher Shared(string directory) =>
        Created.Single(w => !w.IncludeSubdirectories && PathEquals(w.Path, directory));

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal sealed class RaisableWatcher(string directory) : FileSystemWatcher(directory)
    {
        private int _disposeCalls;

        /// <summary>How many times Dispose(true) ran; more than one is a double dispose.</summary>
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Interlocked.Increment(ref _disposeCalls);
            base.Dispose(disposing);
        }

        public void RaiseCreated(string relativeName) =>
            OnCreated(new FileSystemEventArgs(WatcherChangeTypes.Created, Path, relativeName));

        public void RaiseChanged(string relativeName) =>
            OnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, Path, relativeName));

        public void RaiseDeleted(string relativeName) =>
            OnDeleted(new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path, relativeName));

        public void RaiseRenamed(string oldRelativeName, string relativeName) =>
            OnRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, Path, relativeName, oldRelativeName));

        /// <summary>
        /// Completes when this watcher dispatches a Created event for <paramref name="relativeName"/>.
        /// Subscribed after the service's own handlers, so it runs after them for the same event, and a
        /// watcher dispatches its events serially in arrival order: completion proves every earlier
        /// event's service callbacks have finished. Fails loudly after <paramref name="timeout"/>.
        /// </summary>
        public Func<Task> ArmCreatedFence(string relativeName, TimeSpan timeout)
        {
            var fence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Created += (_, args) =>
            {
                if (string.Equals(args.Name, relativeName, StringComparison.Ordinal))
                    fence.TrySetResult();
            };
            return async () =>
            {
                try
                {
                    await fence.Task.WaitAsync(timeout);
                }
                catch (TimeoutException)
                {
                    throw new AssertFailedException(
                        $"The fence file '{relativeName}' was never reported by the watcher within {timeout}.");
                }
            };
        }
    }
}
