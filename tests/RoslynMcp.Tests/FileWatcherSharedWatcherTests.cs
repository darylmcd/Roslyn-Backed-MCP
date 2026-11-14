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
    public async Task Watch_ManyWorkspacesUnderOneParent_UsesSharedAncestorInstances()
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
            // Request shutdown of unreachable watchers from earlier tests before taking the baseline.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            using var deadline = new CancellationTokenSource(SignalTimeout);
            var baseline = GetInotifyDescriptors();
            while (baseline.Values.Any(string.IsNullOrEmpty))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), deadline.Token);
                baseline = GetInotifyDescriptors();
            }

            WatchBatch();
            var afterFirst = GetInotifyDescriptors();
            WatchBatch();
            var afterSecond = GetInotifyDescriptors();

            // The ancestor watchers are shared, so every further workspace costs exactly one instance.
            Assert.AreEqual(batch, afterSecond.Count - afterFirst.Count,
                $"marginal cost per workspace must be 1 inotify instance (baseline {baseline.Count}, first {afterFirst.Count}, second {afterSecond.Count})");

            // A descriptor number may be reused between snapshots. Its watched inode/device records
            // help distinguish a new watcher from a baseline descriptor with the same number.
            var owned = afterFirst.Concat(afterSecond)
                .Where(pair => !baseline.TryGetValue(pair.Key, out var watches) || watches != pair.Value)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            Assert.IsNotEmpty(owned);

            foreach (var id in ids) watcher.Unwatch(id);

            // Linux FileSystemWatcher.Dispose signals its read loop, which closes the descriptor in its finally.
            var remaining = GetInotifyDescriptors();
            try
            {
                while (remaining.Keys.Any(owned.Contains) || remaining.Count != baseline.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5), deadline.Token);
                    remaining = GetInotifyDescriptors();
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                var stillOpen = remaining.Where(pair => owned.Contains(pair.Key));
                Assert.Fail($"Unwatch must release every owned inotify descriptor and return to baseline {baseline.Count}; observed {remaining.Count}. Still open: {string.Join(", ", stillOpen)}.");
            }

            Assert.AreEqual(baseline.Count, remaining.Count, "Unwatch must return the inotify instance count to baseline.");
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

    private static Dictionary<string, string> GetInotifyDescriptors()
    {
        var descriptors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var descriptorInfo in Directory.EnumerateFiles("/proc/self/fdinfo"))
        {
            try
            {
                var descriptor = Path.GetFileName(descriptorInfo);
                if (new FileInfo($"/proc/self/fd/{descriptor}").LinkTarget == "anon_inode:inotify")
                {
                    // Keep watched inode/device records in failure evidence.
                    var watches = File.ReadLines(descriptorInfo)
                        .Where(line => line.StartsWith("inotify ", StringComparison.Ordinal))
                        .Order(StringComparer.Ordinal);
                    descriptors.Add(descriptor, string.Join("; ", watches));
                }
            }
            catch (IOException)
            {
                // The descriptor closed while enumerating.
            }
        }

        return descriptors;
    }
}
