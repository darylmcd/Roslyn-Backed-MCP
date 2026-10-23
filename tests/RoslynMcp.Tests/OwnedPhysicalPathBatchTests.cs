using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OwnedPhysicalPathBatchTests
{
    [TestMethod]
    public async Task SharedPhysicalAncestor_IsAcquiredOncePerBatchAndRevalidated()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "owned-batch-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        PhysicalDirectoryScope? parent = null;
        PhysicalDirectoryScope? root = null;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        var sharedOpens = 0;
        try
        {
            Directory.CreateDirectory(Path.Combine(issued, "shared", "deep"));
            var paths = Enumerable.Range(0, 8).Select(index => Path.Combine("shared", "deep", "input" + index + ".txt")).ToArray();
            foreach (var path in paths) File.WriteAllText(Path.Combine(issued, path), "owned input bytes");
            parent = PhysicalDirectoryScope.OpenRoute(fixture);
            root = parent.OpenChild("issued") ?? throw new AssertFailedException("The actual native owned root must exist.");
            var forwarded = new PhysicalDirectoryFinalRemovalTests.ForwardScope(root, null, false, null, null,
                (directory, name) =>
                {
                    if (FileSystemPath.Comparer.Equals(directory, issued) && name == "shared") sharedOpens++;
                });
            root = null; // transferred to the forwarding scope, then to the owner
            owner = new PhysicalDirectoryScope.OwnedRoot(parent, forwarded, "issued");
            parent = null;
            owner.AssertPhysicalPaths(paths, CancellationToken.None);
            Assert.IsTrue(sharedOpens > 0, "The counter must observe actual successful native shared-parent opens.");
            foreach (var path in paths) Assert.AreEqual("owned input bytes", File.ReadAllText(Path.Combine(issued, path)));
            Assert.IsTrue(sharedOpens <= 2,
                $"One batch must acquire and revalidate the shared native parent, rather than reopen it for every sibling. Actual opens: {sharedOpens}.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Owned batch regression and native fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => root?.Dispose()),
                CleanupFailureCollector.FromAction(() => parent?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task BatchAdmission_RevalidatesThePublishedNameOfEveryHeldParent()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "batch-parent-" + Guid.NewGuid().ToString("N"));
        var issued = Path.Combine(fixture, "issued");
        var shared = Path.Combine(issued, "shared");
        var retained = Path.Combine(issued, "retained");
        PhysicalDirectoryScope? parent = null, root = null;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        var attempted = false;
        var blocked = false;
        var replaced = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(shared, "deep"));
            File.WriteAllText(Path.Combine(shared, "deep", "input.txt"), "same owned bytes");
            parent = PhysicalDirectoryScope.OpenRoute(fixture);
            root = parent.OpenChild("issued") ?? throw new AssertFailedException("The actual native owned root must exist.");
            var forwarded = new PhysicalDirectoryFinalRemovalTests.ForwardScope(root, null, false, null, null,
                (directory, name) =>
                {
                    if (attempted || !FileSystemPath.Comparer.Equals(directory, issued) || name != "shared") return;
                    attempted = true;
                    try { Directory.Move(shared, retained); }
                    catch (IOException failure) when (OperatingSystem.IsWindows() && (failure.HResult & 0xffff) is 32 or 33)
                    { blocked = true; return; }
                    Directory.CreateDirectory(Path.Combine(shared, "deep"));
                    File.WriteAllText(Path.Combine(shared, "deep", "input.txt"), "same owned bytes");
                    replaced = true;
                });
            root = null;
            owner = new PhysicalDirectoryScope.OwnedRoot(parent, forwarded, "issued");
            parent = null;
            Exception? observed = null;
            try { owner.AssertPhysicalPaths([Path.Combine("shared", "deep", "input.txt")], CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.IsTrue(attempted, "The actor must run after the real shared-directory handle has been acquired.");
            Assert.AreEqual(OperatingSystem.IsWindows(), blocked, "Windows must produce a real native sharing violation while the prefix is pinned.");
            Assert.AreEqual(!OperatingSystem.IsWindows(), replaced, "POSIX must exercise a genuinely replaced physical directory name.");
            Assert.AreEqual("same owned bytes", File.ReadAllText(Path.Combine(shared, "deep", "input.txt")));
            if (replaced)
            {
                Assert.AreEqual("same owned bytes", File.ReadAllText(Path.Combine(retained, "deep", "input.txt")));
                Assert.IsInstanceOfType<IOException>(observed, "An old descriptor must not certify the replacement occupying its published name.");
            }
            else Assert.IsNull(observed, "A genuinely blocked replacement leaves the original route admissible.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Batch parent binding and fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => owner?.Dispose()),
                CleanupFailureCollector.FromAction(() => root?.Dispose()),
                CleanupFailureCollector.FromAction(() => parent?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }
}
