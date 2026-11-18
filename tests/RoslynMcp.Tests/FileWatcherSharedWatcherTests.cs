using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the inotify footprint of <see cref="FileWatcherService"/>. On Linux every
/// <see cref="FileSystemWatcher"/> owns one inotify instance and the default per-user cap is 128, so a
/// workspace must cost one tree watcher plus shared ancestor watchers rather than a dozen private ones.
/// The behavior tests run on every platform because the watcher topology is platform independent.
/// The inotify footprint test measures descriptors inside a dedicated child process.
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

        var root = Path.Combine(TestTempRoot.Current, $"inotify-footprint-{Guid.NewGuid():N}");
        var probe = Path.Combine(AppContext.BaseDirectory, "InotifyWatcherProbe.dll");
        try
        {
            var result = await PwshScriptRunner.RunExecutableAsync(
                "dotnet", [probe, root], environment: null,
                timeout: SignalTimeout + TimeSpan.FromSeconds(15),
                description: "Linux inotify watcher footprint probe");
            Console.WriteLine(result.AllOutput);
            Assert.AreEqual(0, result.ExitCode, result.AllOutput);
            StringAssert.Contains(result.StdOut, "marginal=10 final=1", result.AllOutput);
            StringAssert.Contains(result.StdOut, "remainingOwned=0 emptyBaselineRetained=true", result.AllOutput);
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(root);
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
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var id = Guid.NewGuid().ToString("N");
        var raised = 0;
        watcher.WorkspaceRootMissing += _ => Interlocked.Increment(ref raised);
        var solution = Path.Combine(root, "Sample.slnx");
        try
        {
            File.WriteAllText(solution, "<Solution />");
            watcher.Watch(id, solution);
            // The stale flag cannot fence this: the overwrite marks the workspace stale itself. A watcher
            // dispatches events serially, so the marker's Created callback (run after the service's own
            // handlers) can only fire once the overwrite's Deleted/Renamed callbacks have finished.
            var markerSeen = factory.Tree(root).ArmCreatedFence("Marker.txt", SignalTimeout);

            // An atomic save (write a temp file, rename it over the solution) leaves the file in place.
            var temp = Path.Combine(root, "Sample.slnx.tmp");
            File.WriteAllText(temp, "<Solution />");
            File.Move(temp, solution, overwrite: true);
            File.WriteAllText(Path.Combine(root, "Marker.txt"), "marker");
            await markerSeen();

            Assert.AreEqual(0, Volatile.Read(ref raised));
        }
        finally
        {
            watcher.Unwatch(id);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TreeCallbacks_WorkspaceFileStillPresent_DoNotRaiseRootMissing()
    {
        var root = NewTempRoot();
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var id = Guid.NewGuid().ToString("N");
        var raised = 0;
        watcher.WorkspaceRootMissing += _ => Interlocked.Increment(ref raised);
        try
        {
            File.WriteAllText(Path.Combine(root, "Sample.slnx"), "<Solution />");
            watcher.Watch(id, Path.Combine(root, "Sample.slnx"));
            var tree = factory.Tree(root);

            // Windows reports the file an overwriting rename replaced as Deleted while it still exists.
            tree.RaiseDeleted("Sample.slnx");
            tree.RaiseRenamed("Other.slnx", "Sample.slnx");
            tree.RaiseRenamed("Sample.slnx.tmp", "Sample.slnx");

            Assert.AreEqual(0, Volatile.Read(ref raised));
        }
        finally
        {
            watcher.Unwatch(id);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void TreeCallbacks_WorkspaceFileGoneOrRenamedAway_RaiseRootMissing()
    {
        var root = NewTempRoot();
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var id = Guid.NewGuid().ToString("N");
        var raised = new List<string>();
        watcher.WorkspaceRootMissing += workspaceId => { lock (raised) raised.Add(workspaceId); };
        try
        {
            // The file never existed on disk, so a Deleted event for it is a real removal.
            watcher.Watch(id, Path.Combine(root, "Sample.slnx"));
            var tree = factory.Tree(root);

            tree.RaiseDeleted("Sample.slnx");
            lock (raised) CollectionAssert.AreEqual(new[] { id }, raised);

            tree.RaiseRenamed("Sample.slnx", "Renamed.slnx");
            lock (raised) CollectionAssert.AreEqual(new[] { id, id }, raised);
        }
        finally
        {
            watcher.Unwatch(id);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Watch_InotifyLimitOnSecondAncestor_LeavesNoEntryNoSharedWatcherAndDisposesEach()
    {
        var parent = NewTempRoot();
        var root = Path.Combine(parent, "ws");
        Directory.CreateDirectory(root);
        // Creation 1 is the tree watcher, 2 the first ancestor's shared watcher, 3 the failing ancestor.
        var factory = new RaisableWatcherFactory((ordinal, _) => ordinal == 3
            ? new IOException("The configured user limit (128) on the number of inotify instances has been reached.")
            : null);
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var failure = Assert.ThrowsExactly<IOException>(() => watcher.Watch(id, Path.Combine(root, "Sample.slnx")));

            StringAssert.Contains(failure.Message, "inotify");
            Assert.IsFalse(watcher.IsWatchedForTesting(id));
            Assert.AreEqual(0, watcher.SharedWatcherCountForTesting);
            Assert.AreEqual(2, factory.Created.Count);
            foreach (var created in factory.Created)
                Assert.AreEqual(1, created.DisposeCalls, $"{created.Path} must be disposed exactly once.");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void DescribeStartFailure_NamesInotifyLimitOnlyForIoFailuresOnLinux()
    {
        var io = new IOException("limit reached");

        var described = FileWatcherService.DescribeStartFailure(io, "ws1", isLinux: true);

        Assert.IsNotNull(described);
        StringAssert.Contains(described.Message, "fs.inotify.max_user_instances");
        StringAssert.Contains(described.Message, "ws1");
        StringAssert.Contains(described.Message, "limit reached");
        Assert.AreSame(io, described.InnerException);
        Assert.IsNull(FileWatcherService.DescribeStartFailure(io, "ws1", isLinux: false));
        Assert.IsNull(FileWatcherService.DescribeStartFailure(new UnauthorizedAccessException(), "ws1", isLinux: true));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NestedWorkspace_ChildDirectoryRemoved_RetiresOnlyTheChild(bool rename)
    {
        var parent = NewTempRoot();
        var outerRoot = Path.Combine(parent, "outer");
        var innerRoot = Path.Combine(outerRoot, "inner");
        Directory.CreateDirectory(innerRoot);
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var outer = Guid.NewGuid().ToString("N");
        var inner = Guid.NewGuid().ToString("N");
        var raised = new List<string>();
        watcher.WorkspaceRootMissing += id => { lock (raised) raised.Add(id); };
        try
        {
            watcher.Watch(outer, Path.Combine(outerRoot, "Sample.slnx"));
            watcher.Watch(inner, Path.Combine(innerRoot, "Sample.slnx"));

            // The outer root is both a workspace root and the inner workspace's ancestor directory.
            var outerAsAncestor = factory.Shared(outerRoot);
            if (rename)
                outerAsAncestor.RaiseRenamed("inner", "inner-moved");
            else
                outerAsAncestor.RaiseDeleted("inner");

            lock (raised) CollectionAssert.AreEqual(new[] { inner }, raised);
        }
        finally
        {
            watcher.Unwatch(inner);
            watcher.Unwatch(outer);
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void Dispose_DisposesEveryWatcherAndLeavesNoSharedWatcherRegistered()
    {
        var parent = NewTempRoot();
        var factory = new RaisableWatcherFactory();
        var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var root = Path.Combine(parent, $"ws{i}");
                Directory.CreateDirectory(root);
                watcher.Watch(Guid.NewGuid().ToString("N"), Path.Combine(root, "Sample.slnx"));
            }

            Assert.IsTrue(watcher.SharedWatcherCountForTesting > 0);
            Assert.IsTrue(factory.Created.Count > 3);

            watcher.Dispose();

            Assert.AreEqual(0, watcher.SharedWatcherCountForTesting);
            foreach (var created in factory.Created)
                Assert.AreEqual(1, created.DisposeCalls, $"{created.Path} must be disposed exactly once.");
        }
        finally
        {
            watcher.Dispose();
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public async Task ConcurrentWatchAndUnwatch_UnderOneParent_NeverLeaksOrDoubleDisposes()
    {
        const int workers = 6;
        const int iterations = 15;
        var parent = NewTempRoot();
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        try
        {
            using var start = new Barrier(workers);
            var tasks = Enumerable.Range(0, workers).Select(worker =>
            {
                var root = Path.Combine(parent, $"ws{worker}");
                Directory.CreateDirectory(root);
                var solution = Path.Combine(root, "Sample.slnx");
                var id = Guid.NewGuid().ToString("N");
                return Task.Factory.StartNew(() =>
                {
                    start.SignalAndWait();
                    for (var i = 0; i < iterations; i++)
                    {
                        // Every worker subscribes to and releases the same ancestor directories.
                        watcher.Watch(id, solution);
                        watcher.Unwatch(id);
                    }
                }, TaskCreationOptions.LongRunning);
            }).ToArray();

            await Task.WhenAll(tasks).WaitAsync(SignalTimeout);

            Assert.AreEqual(0, watcher.SharedWatcherCountForTesting);
            Assert.IsTrue(factory.Created.Count >= workers * iterations);
            foreach (var created in factory.Created)
                Assert.AreEqual(1, created.DisposeCalls, $"{created.Path} must be disposed exactly once.");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void TreeWatcher_EventsUnderObjBinAndGit_DoNotMarkStale()
    {
        var root = NewTempRoot();
        var factory = new RaisableWatcherFactory();
        using var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance, factory.Create);
        var id = Guid.NewGuid().ToString("N");
        try
        {
            watcher.Watch(id, Path.Combine(root, "Sample.slnx"));
            var tree = factory.Tree(root);

            foreach (var excluded in new[] { "obj", "bin", ".git", Path.Combine("src", "obj"), Path.Combine("src", "bin") })
            {
                tree.RaiseCreated(Path.Combine(excluded, "Generated.cs"));
                tree.RaiseChanged(Path.Combine(excluded, "Generated.cs"));
                tree.RaiseDeleted(Path.Combine(excluded, "Generated.cs"));
                tree.RaiseRenamed(Path.Combine(excluded, "Old.cs"), Path.Combine(excluded, "Generated.cs"));
                Assert.IsFalse(watcher.IsStale(id), $"An event under '{excluded}' must not mark the workspace stale.");
            }

            tree.RaiseCreated(Path.Combine("src", "Real.cs"));

            Assert.IsTrue(watcher.IsStale(id));
            Assert.AreEqual(StaleReasons.ExternalEdit, watcher.GetStaleReason(id));
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
}
