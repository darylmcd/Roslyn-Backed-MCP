using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// FileSystemWatcher-backed implementation of <see cref="IFileWatcherService"/>.
/// Flags a workspace as stale when a tracked <c>.cs</c>/<c>.csproj</c>/<c>.props</c>/
/// <c>.targets</c>/<c>.sln</c>/<c>.slnx</c>/<c>.editorconfig</c> file changes, and records a reason so
/// <c>workspace_status</c> can distinguish server-generated writes (<c>apply</c> /
/// <c>restore</c>) from genuinely external edits (<c>external-edit</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Attribution rule</strong> (<c>workspace-stale-after-external-edit-feedback</c>):
/// watcher-driven marks record <see cref="StaleReasons.ExternalEdit"/> except delayed events
/// whose .editorconfig bytes match a committed server write. Those preserve Apply. Other
/// server apply / restore paths call <see cref="MarkStale"/> explicitly. That method is
/// <em>last-writer-wins</em> inside a single stale window
/// (<see cref="ClearStale"/> resets to a clean slate); two independent events do not
/// compose. Callers that need to refuse on genuine external drift
/// (<c>change_signature_preview</c> and friends) query <see cref="GetStaleReason"/>; server
/// apply paths must coordinate their own watcher events when attribution matters.
/// </para>
/// <para>
/// <strong>CPU cost</strong>: purely event-driven; no periodic scans. <c>FileSystemWatcher</c>
/// can miss rapid-fire batched edits (buffer overflow), but the dominant risk is <em>over</em>-
/// firing (e.g. during <c>dotnet restore</c>'s <c>obj/</c> churn). We filter out <c>obj/</c>,
/// <c>bin/</c>, and <c>.git/</c> at ingress so a restore does not churn the flag for every file
/// it touches; the flag is a single <see langword="volatile"/> read on the hot path.
/// </para>
/// </remarks>
internal interface IEditorConfigWriteCoordinator
{
    T RunOwnedWrite<T>(string workspaceId, string path, Func<EditorConfigFileTransaction, T> write);
}

/// <summary>Holds the config file against competing writes while its snapshot is parsed and changed.</summary>
internal sealed class EditorConfigFileTransaction : IDisposable
{
    private readonly FileStream _stream;

    internal EditorConfigFileTransaction(string path, byte[]? expectedBytes)
    {
        CreatedNewFile = expectedBytes is null;
        _stream = new FileStream(path,
            CreatedNewFile ? FileMode.CreateNew : FileMode.Open,
            FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // POSIX sharing flags do not exclude another process. Cooperating
                // writers can honor this advisory range lock for the transaction.
                _stream.Lock(0, long.MaxValue);
            }
            OriginalBytes = CreatedNewFile ? null : ReadCurrentBytes();
            if (!EditorConfigBytes.AreEqual(expectedBytes, OriginalBytes))
            {
                throw new EditorConfigConcurrentEditException();
            }
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    internal bool CreatedNewFile { get; }
    internal byte[]? OriginalBytes { get; }
    internal bool WriteStarted { get; private set; }
    internal byte[]? OwnedBytes { get; private set; }

    internal void WriteBytes(byte[] bytes)
    {
        // On POSIX an uncooperative writer can ignore the advisory lock. Check the
        // held inode again immediately before changing it, and refuse known drift.
        var expectedBytes = WriteStarted ? OwnedBytes : CreatedNewFile ? [] : OriginalBytes;
        if (!EditorConfigBytes.AreEqual(expectedBytes, ReadCurrentBytes()))
        {
            throw new EditorConfigConcurrentEditException();
        }
        WriteStarted = true;
        try
        {
            _stream.Position = 0;
            _stream.SetLength(0);
            _stream.Write(bytes);
            _stream.Flush(flushToDisk: true);
        }
        finally
        {
            // A failed write may leave a partial file. Capture those exact server-owned
            // bytes before releasing the handle so a later external replacement is distinct.
            OwnedBytes = ReadCurrentBytes();
        }
    }

    internal void RestoreOriginalBytes()
    {
        try
        {
            _stream.Position = 0;
            _stream.SetLength(0);
            if (OriginalBytes is not null)
            {
                _stream.Write(OriginalBytes);
            }
            _stream.Flush(flushToDisk: true);
        }
        finally
        {
            // Keep the last bytes actually written even when restoration itself fails.
            OwnedBytes = ReadCurrentBytes();
        }
    }

    private byte[] ReadCurrentBytes()
    {
        _stream.Position = 0;
        using var buffer = new MemoryStream();
        _stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>Byte-exact comparison of .editorconfig snapshots shared by the transaction and the watcher.</summary>
internal static class EditorConfigBytes
{
    internal static bool AreEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}

internal sealed class EditorConfigConcurrentEditException : IOException
{
    internal EditorConfigConcurrentEditException()
        : base("The .editorconfig changed during the coordinated write.") { }

    internal EditorConfigConcurrentEditException(Exception inner)
        : base("The .editorconfig could not be exclusively opened for the coordinated write.", inner) { }
}

internal sealed class EditorConfigUnrecoveredWriteException : IOException
{
    internal EditorConfigUnrecoveredWriteException(Exception inner)
        : base("The .editorconfig write failed and its changed bytes could not be restored.", inner) { }
}

public sealed class FileWatcherService(ILogger<FileWatcherService> logger) : IFileWatcherService, IEditorConfigWriteCoordinator
{
    // A watcher comparison can overlap an undo that deletes a newly created config.
    // File.ReadAllBytes uses FileShare.Read, which makes that delete fail on Windows.
    internal static FileStream OpenConfigReadStream(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

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

        entry.AddWatcher(CreateWatcher(directory, ".editorconfig", entry));
        // A config above the solution directory also applies to its documents. Watch each
        // ancestor's single config name so external policy changes invalidate this workspace.
        for (var ancestor = Path.GetDirectoryName(directory); ancestor is not null;
             ancestor = Path.GetDirectoryName(ancestor))
        {
            if (Directory.Exists(ancestor))
            {
                entry.AddWatcher(CreateWatcher(ancestor, ".editorconfig", entry, recursive: false));
            }
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
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_watchers.TryGetValue(workspaceId, out var entry))
        {
            entry.MarkStaleWithReason(reason);
        }
    }

    T IEditorConfigWriteCoordinator.RunOwnedWrite<T>(
        string workspaceId, string path, Func<EditorConfigFileTransaction, T> write)
    {
        if (!_watchers.TryGetValue(workspaceId, out var entry))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not watched.");
        }

        return entry.RunOwnedWrite(path, write);
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

    private static FileSystemWatcher CreateWatcher(
        string directory, string filter, WatcherEntry entry, bool recursive = true)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = filter,
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, args) => MarkStaleIfRelevant(entry, args.FullPath, recursive);
        watcher.Created += (_, args) => MarkStaleIfRelevant(entry, args.FullPath, recursive);
        watcher.Deleted += (_, args) => MarkStaleIfRelevant(entry, args.FullPath, recursive);
        watcher.Renamed += (_, args) => MarkStaleIfRelevant(entry, args.FullPath, recursive);

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
                    "Workspace-root-missing subscriber failed for workspace {WorkspaceId}; exceptionType={ExceptionType}",
                    workspaceId,
                    exception.GetType().FullName);
            }
        }
    }

    private static void MarkStaleIfRelevant(WatcherEntry entry, string fullPath, bool checkExcludedPaths)
    {
        if (checkExcludedPaths && ShouldIgnorePath(fullPath, entry.RootDirectory))
        {
            return;
        }

        // Compare config bytes with the committed owned write under the same state lock.
        // Delayed events for those bytes preserve Apply; different bytes are ExternalEdit.
        entry.MarkFileChange(fullPath);
    }

    private static bool ShouldIgnorePath(string fullPath, string rootDirectory)
    {
        // Exclusions are relative to the watched workspace root. Inspecting the absolute path
        // makes a workspace rooted at <repo>/.worktrees/<id> ignore every event because the
        // root's own ancestry contains ".worktrees". The relative path still excludes nested
        // worktrees from a primary checkout without suppressing the worktree checkout itself.
        var relativePath = Path.GetRelativePath(rootDirectory, fullPath);
        return ContainsPathSegment(relativePath, "obj") ||
               ContainsPathSegment(relativePath, "bin") ||
               ContainsPathSegment(relativePath, ".git") ||
               ContainsPathSegment(relativePath, ".roslynmcp") ||
               ContainsPathSegment(relativePath, ".worktrees");
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
        private readonly Dictionary<string, byte[]?> _ownedConfigBytes = new(FileSystemPath.Comparer);
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

        public T RunOwnedWrite<T>(string path, Func<EditorConfigFileTransaction, T> write)
        {
            var physicalPath = PhysicalPathResolver.Resolve(path);
            lock (_reasonLock)
            {
                var before = ReadBytesOrNull(physicalPath);
                if (_ownedConfigBytes.TryGetValue(physicalPath, out var previous) &&
                    !EditorConfigBytes.AreEqual(previous, before))
                {
                    _ownedConfigBytes.Remove(physicalPath);
                    MarkStaleWithReason(StaleReasons.ExternalEdit);
                }
                EditorConfigFileTransaction? transaction = null;
                try
                {
                    try
                    {
                        transaction = new EditorConfigFileTransaction(physicalPath, before);
                    }
                    catch (IOException ex)
                    {
                        throw new EditorConfigConcurrentEditException(ex);
                    }
                    var result = write(transaction);
                    var writtenBytes = transaction.OwnedBytes
                        ?? throw new InvalidOperationException("The coordinated .editorconfig write did not write bytes.");
                    transaction.Dispose();
                    transaction = null;
                    _ownedConfigBytes[physicalPath] = writtenBytes;
                    var readable = TryReadBytesOrNull(physicalPath, out var current);
                    if (!readable || !EditorConfigBytes.AreEqual(writtenBytes, current))
                    {
                        MarkStaleWithReason(StaleReasons.ExternalEdit);
                        throw new EditorConfigConcurrentEditException();
                    }
                    else if (_staleReason != StaleReasons.ExternalEdit)
                    {
                        MarkStaleWithReason(StaleReasons.Apply);
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    var writeStarted = transaction?.WriteStarted == true;
                    var createdWithoutWrite = transaction is { CreatedNewFile: true, WriteStarted: false };
                    var pathMatchedOwnedBytes = false;
                    Exception? restorationFailure = null;
                    if (writeStarted && transaction?.OwnedBytes is { } writtenBytes)
                    {
                        try
                        {
                            // Check the pathname before restoring the held handle: on POSIX
                            // a rename can replace the pathname while the old inode is locked.
                            pathMatchedOwnedBytes = EditorConfigBytes.AreEqual(writtenBytes, ReadBytesOrNull(physicalPath));
                            if (pathMatchedOwnedBytes)
                            {
                                transaction.RestoreOriginalBytes();
                            }
                        }
                        catch (Exception restoreEx) when (restoreEx is IOException or UnauthorizedAccessException or ObjectDisposedException)
                        {
                            restorationFailure = restoreEx;
                        }
                    }
                    var ownedBytes = transaction?.OwnedBytes;
                    transaction?.Dispose();
                    var readable = TryReadBytesOrNull(physicalPath, out var after);
                    Exception? cleanupFailure = null;
                    if ((createdWithoutWrite || (pathMatchedOwnedBytes && transaction?.CreatedNewFile == true)) &&
                        after is { Length: 0 })
                    {
                        // Opening a new file is itself a mutation. If the callback failed
                        // before writing, remove that empty file instead of stranding it.
                        try
                        {
                            File.Delete(physicalPath);
                            after = null;
                        }
                        catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                        {
                            cleanupFailure = cleanupEx;
                        }
                    }
                    if (!readable ||
                        (writeStarted && !pathMatchedOwnedBytes && !EditorConfigBytes.AreEqual(before, after)) ||
                        (writeStarted && !EditorConfigBytes.AreEqual(ownedBytes, after) && !EditorConfigBytes.AreEqual(before, after)) ||
                        (!writeStarted && !createdWithoutWrite && !EditorConfigBytes.AreEqual(before, after)) ||
                        (!writeStarted && createdWithoutWrite && after is { Length: > 0 }))
                    {
                        _ownedConfigBytes.Remove(physicalPath);
                        MarkStaleWithReason(StaleReasons.ExternalEdit);
                    }
                    else if (readable && EditorConfigBytes.AreEqual(before, after))
                    {
                        // A failed write can still queue a watcher event. Its unchanged bytes
                        // are not a workspace change and must not turn the workspace stale.
                        _ownedConfigBytes[physicalPath] = after;
                    }
                    else
                    {
                        _ownedConfigBytes[physicalPath] = after;
                        if (_staleReason != StaleReasons.ExternalEdit)
                        {
                            MarkStaleWithReason(StaleReasons.Apply);
                        }
                        var failures = new List<Exception> { ex };
                        if (restorationFailure is not null)
                        {
                            failures.Add(restorationFailure);
                        }
                        if (cleanupFailure is not null)
                        {
                            failures.Add(cleanupFailure);
                        }
                        throw new EditorConfigUnrecoveredWriteException(
                            failures.Count == 1 ? ex : new AggregateException(failures));
                    }
                    if (cleanupFailure is not null)
                    {
                        throw new AggregateException(ex, cleanupFailure);
                    }
                    throw;
                }
            }
        }

        public void MarkFileChange(string path)
        {
            lock (_reasonLock)
            {
                if (Path.GetFileName(path).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase))
                {
                    string physicalPath;
                    try
                    {
                        physicalPath = PhysicalPathResolver.Resolve(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        MarkStaleWithReason(StaleReasons.ExternalEdit);
                        return;
                    }
                    var readable = TryReadBytesOrNull(physicalPath, out var current);
                    if (readable && _ownedConfigBytes.TryGetValue(physicalPath, out var committed) &&
                        EditorConfigBytes.AreEqual(committed, current))
                    {
                        return;
                    }
                    _ownedConfigBytes.Remove(physicalPath);
                }

                MarkStaleWithReason(StaleReasons.ExternalEdit);
            }
        }

        private static byte[]? ReadBytesOrNull(string path)
        {
            if (!File.Exists(path)) return null;

            using var stream = OpenConfigReadStream(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        // Unreadable (sharing or permission failure) is distinct from absent: callers treat it as drift.
        private static bool TryReadBytesOrNull(string path, out byte[]? bytes)
        {
            try
            {
                bytes = ReadBytesOrNull(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bytes = null;
                return false;
            }
        }

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
