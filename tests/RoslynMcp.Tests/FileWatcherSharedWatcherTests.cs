using Microsoft.Extensions.Logging.Abstractions;
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
}
