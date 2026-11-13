using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the inotify footprint of <see cref="FileWatcherService"/>. On Linux every
/// <see cref="FileSystemWatcher"/> owns one inotify instance and the default per-user cap is 128, so a
/// workspace must cost one tree watcher plus shared ancestor watchers rather than a dozen private ones.
/// The behavior tests run on every platform because the watcher topology is platform independent.
/// The inotify descriptor count is process-wide, so the class runs exclusively: concurrent classes that hold
/// their own watchers would otherwise move the baseline.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FileWatcherSharedWatcherTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void Watch_ManyWorkspacesUnderOneParent_UsesSharedAncestorInstances()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Counts Linux inotify descriptors.");
        }

        const int batch = 10;
        var parent = NewTempRoot();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var ids = new List<string>();
        void WatchBatch()
        {
            for (var i = 0; i < batch; i++)
            {
                var root = Path.Combine(parent, $"ws{ids.Count}");
                Directory.CreateDirectory(root);
                var id = Guid.NewGuid().ToString("N");
                ids.Add(id);
                watcher.Watch(id, Path.Combine(root, "Sample.slnx"));
            }
        }

        try
        {
            // Finalize watchers leaked by earlier tests so they cannot close descriptors mid-measurement.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var baseline = CountInotifyInstances();
            WatchBatch();
            var afterFirst = CountInotifyInstances();
            WatchBatch();
            var afterSecond = CountInotifyInstances();

            // The ancestor watchers are shared, so every further workspace costs exactly one instance.
            Assert.AreEqual(batch, afterSecond - afterFirst,
                $"marginal cost per workspace must be 1 inotify instance (baseline {baseline}, first {afterFirst}, second {afterSecond})");

            foreach (var id in ids) watcher.Unwatch(id);
            Assert.AreEqual(baseline, CountInotifyInstances(), "Unwatch must release every inotify instance.");
        }
        finally
        {
            foreach (var id in ids) watcher.Unwatch(id);
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public async Task SharedAncestorEditorConfig_MarksEveryWorkspaceBeneathItStale()
    {
        var parent = NewTempRoot();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var first = Guid.NewGuid().ToString("N");
        var second = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.Combine(parent, "a"));
            Directory.CreateDirectory(Path.Combine(parent, "b"));
            watcher.Watch(first, Path.Combine(parent, "a", "Sample.slnx"));
            watcher.Watch(second, Path.Combine(parent, "b", "Sample.slnx"));

            File.WriteAllText(Path.Combine(parent, ".editorconfig"), "root = true\n");

            using var cts = new CancellationTokenSource(SignalTimeout);
            await Task.WhenAll(watcher.WaitForStaleAsync(first, cts.Token), watcher.WaitForStaleAsync(second, cts.Token));
            Assert.IsTrue(watcher.IsStale(first));
            Assert.IsTrue(watcher.IsStale(second));
        }
        finally
        {
            watcher.Unwatch(first);
            watcher.Unwatch(second);
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public async Task DeletingOneRoot_RetiresOnlyThatWorkspaceAndKeepsSiblingWatched()
    {
        var parent = NewTempRoot();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var gone = Guid.NewGuid().ToString("N");
        var kept = Guid.NewGuid().ToString("N");
        var missing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var raised = new List<string>();
        watcher.WorkspaceRootMissing += id =>
        {
            lock (raised) raised.Add(id);
            missing.TrySetResult(id);
        };
        try
        {
            var goneRoot = Path.Combine(parent, "gone");
            var keptRoot = Path.Combine(parent, "kept");
            Directory.CreateDirectory(goneRoot);
            Directory.CreateDirectory(keptRoot);
            watcher.Watch(gone, Path.Combine(goneRoot, "Sample.slnx"));
            watcher.Watch(kept, Path.Combine(keptRoot, "Sample.slnx"));

            Directory.Delete(goneRoot, recursive: true);

            var signalled = await missing.Task.WaitAsync(SignalTimeout);
            Assert.AreEqual(gone, signalled);
            lock (raised) CollectionAssert.DoesNotContain(raised, kept);

            // The sibling's ancestor subscription survived the other workspace's removal.
            watcher.Unwatch(gone);
            File.WriteAllText(Path.Combine(parent, ".editorconfig"), "root = true\n");
            using var cts = new CancellationTokenSource(SignalTimeout);
            await watcher.WaitForStaleAsync(kept, cts.Token);
            Assert.IsTrue(watcher.IsStale(kept));
        }
        finally
        {
            watcher.Unwatch(gone);
            watcher.Unwatch(kept);
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public async Task DeletingTheWorkspaceFile_RaisesRootMissing()
    {
        var root = NewTempRoot();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var id = Guid.NewGuid().ToString("N");
        var missing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.WorkspaceRootMissing += workspaceId => missing.TrySetResult(workspaceId);
        var solution = Path.Combine(root, "Sample.slnx");
        try
        {
            File.WriteAllText(solution, "<Solution />");
            watcher.Watch(id, solution);

            File.Delete(solution);

            Assert.AreEqual(id, await missing.Task.WaitAsync(SignalTimeout));
        }
        finally
        {
            watcher.Unwatch(id);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RenamingAnotherFileOverTheWorkspaceFile_DoesNotRaiseRootMissing()
    {
        var root = NewTempRoot();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var id = Guid.NewGuid().ToString("N");
        var raised = 0;
        watcher.WorkspaceRootMissing += _ => Interlocked.Increment(ref raised);
        var solution = Path.Combine(root, "Sample.slnx");
        try
        {
            File.WriteAllText(solution, "<Solution />");
            watcher.Watch(id, solution);

            // An atomic save (write a temp file, rename it over the solution) leaves the file in place.
            var temp = Path.Combine(root, "Sample.slnx.tmp");
            File.WriteAllText(temp, "<Solution />");
            File.Move(temp, solution, overwrite: true);
            // Events arrive in order, so a later watched change proves the rename was already processed.
            File.WriteAllText(Path.Combine(root, "Marker.cs"), "class Marker {}");

            using var cts = new CancellationTokenSource(SignalTimeout);
            await watcher.WaitForStaleAsync(id, cts.Token);
            await Task.Delay(500);
            Assert.AreEqual(0, Volatile.Read(ref raised));
        }
        finally
        {
            watcher.Unwatch(id);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"roslyn-shared-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static int CountInotifyInstances()
    {
        var count = 0;
        foreach (var descriptor in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            try
            {
                if (new FileInfo(descriptor).LinkTarget?.Contains("inotify", StringComparison.Ordinal) == true) count++;
            }
            catch (IOException)
            {
                // The descriptor closed while enumerating.
            }
        }

        return count;
    }
}
