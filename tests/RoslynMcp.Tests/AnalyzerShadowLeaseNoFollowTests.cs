using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowLeaseNoFollowTests
{
    [TestMethod]
    public void Dispose_RemovesRootWithDescendantJunction_LeavesTargetIntact()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Directory junctions are a Windows reparse-point type.");
            return;
        }

        var root = NewRoot("junction");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "keep.txt");
            Directory.CreateDirectory(Path.Combine(shadowRoot, "nested"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(shadowRoot, "copy.dll"), "fixture");
            File.WriteAllText(targetFile, "external");
            WindowsDirectoryJunction.Create(Path.Combine(shadowRoot, "nested", "link"), target);

            var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                shadowRoot, [], 0, NullLogger.Instance);
            lease.Dispose();

            Assert.IsFalse(Directory.Exists(shadowRoot), "The owned shadow root must be removed, not leaked.");
            Assert.IsTrue(File.Exists(targetFile), "The junction target must be untouched.");
            Assert.AreEqual("external", File.ReadAllText(targetFile));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Sweep_RemovesStaleLeaseWithDescendantJunction_LeavesTargetIntact()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Directory junctions are a Windows reparse-point type.");
            return;
        }

        var root = NewRoot("sweep");
        try
        {
            var shared = Path.Combine(root, "shared");
            var lease = Path.Combine(shared, "workspace-999999-" + Guid.NewGuid().ToString("N"), "stale-lease");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "keep.txt");
            Directory.CreateDirectory(Path.Combine(lease, "nested"));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(lease, "copy.dll"), "fixture");
            File.WriteAllText(targetFile, "external");
            WindowsDirectoryJunction.Create(Path.Combine(lease, "nested", "link"), target);
            var stamp = DateTime.UtcNow.AddDays(-3);
            Directory.SetCreationTimeUtc(lease, stamp);
            Directory.SetLastWriteTimeUtc(lease, stamp);

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.IsFalse(Directory.Exists(lease), "The stale owned lease must be swept, not leaked.");
            Assert.IsTrue(File.Exists(targetFile), "The junction target must be untouched.");
            Assert.AreEqual("external", File.ReadAllText(targetFile));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Delete_SwapToJunctionBeforeEnumerate_DoesNotTouchTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Directory junctions are a Windows reparse-point type.");
            return;
        }

        var root = NewRoot("swap");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var sub = Path.Combine(shadowRoot, "sub");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "keep.txt");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "copy.dll"), "fixture");
            Directory.CreateDirectory(target);
            File.WriteAllText(targetFile, "external");

            var hookRan = false;
            void Swap(string directory)
            {
                // Replace the already-classified directory with a junction right before it is
                // enumerated. Windows refuses the swap while the walker holds the directory
                // open; the walker must never follow the junction if the swap does land.
                if (!OperatingSystem.IsWindows() || hookRan || !string.Equals(directory, sub, StringComparison.OrdinalIgnoreCase)) return;
                hookRan = true;
                try
                {
                    Directory.Delete(sub, recursive: true);
                    WindowsDirectoryJunction.Create(sub, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held open: the swap was denied, which is the protected outcome.
                }
            }

            try
            {
                NoFollowDirectoryDeleter.Delete(shadowRoot, CancellationToken.None, Swap, NullLogger.Instance);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A failed delete is acceptable; touching the target is not.
            }

            Assert.IsTrue(hookRan, "The beforeEnumerate hook must run for the child directory.");
            Assert.IsTrue(File.Exists(targetFile), "The swapped-in junction target must be untouched.");
            Assert.AreEqual("external", File.ReadAllText(targetFile));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Dispose_RemovesRootWithDescendantSymlink_LeavesTargetIntact()
    {
        var root = NewRoot("symlink");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "keep.txt");
            Directory.CreateDirectory(shadowRoot);
            Directory.CreateDirectory(target);
            File.WriteAllText(targetFile, "external");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(shadowRoot, "link"), target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Assert.Inconclusive(
                    "Creating a directory symlink requires SeCreateSymbolicLinkPrivilege or Developer Mode on Windows: " + ex.Message);
            }

            var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                shadowRoot, [], 0, NullLogger.Instance);
            lease.Dispose();

            Assert.IsFalse(Directory.Exists(shadowRoot));
            Assert.IsTrue(File.Exists(targetFile));
            Assert.AreEqual("external", File.ReadAllText(targetFile));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Delete_RenameEnumeratedParentAndPlantJunction_DoesNotTouchTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Directory junctions are a Windows reparse-point type.");
            return;
        }

        var root = NewRoot("rename-parent");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var sub = Path.Combine(shadowRoot, "sub");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "copy.dll");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "copy.dll"), "fixture");
            Directory.CreateDirectory(target);
            File.WriteAllText(targetFile, "external");

            var hookRan = false;
            void RenameAndPlant(string directory)
            {
                // The children of 'sub' are already enumerated and queued by path. Moving 'sub'
                // away and planting a junction at its name would redirect every queued child path
                // into the external target unless the walker still holds 'sub'.
                if (!OperatingSystem.IsWindows() || hookRan || !string.Equals(directory, sub, StringComparison.OrdinalIgnoreCase)) return;
                hookRan = true;
                try
                {
                    Directory.Move(sub, sub + "-moved");
                    WindowsDirectoryJunction.Create(sub, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held open: the rename was denied, which is the protected outcome.
                }
            }

            try
            {
                NoFollowDirectoryDeleter.Delete(
                    shadowRoot, CancellationToken.None, afterEnumerate: RenameAndPlant, logger: NullLogger.Instance);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A failed delete is acceptable; touching the target is not.
            }

            Assert.IsTrue(hookRan, "The afterEnumerate hook must run for the child directory.");
            Assert.IsTrue(File.Exists(targetFile), "A queued child path must never resolve through a planted junction.");
            Assert.AreEqual("external", File.ReadAllText(targetFile));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Delete_SwapLeafBeforeMutate_DoesNotTouchTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Directory junctions are a Windows reparse-point type.");
            return;
        }

        var root = NewRoot("swap-leaf");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var leaf = Path.Combine(shadowRoot, "copy.dll");
            var target = Path.Combine(root, "external-target");
            var targetFile = Path.Combine(target, "keep.txt");
            Directory.CreateDirectory(shadowRoot);
            File.WriteAllText(leaf, "fixture");
            File.SetAttributes(leaf, FileAttributes.ReadOnly);
            Directory.CreateDirectory(target);
            File.WriteAllText(targetFile, "external");
            File.SetAttributes(target, FileAttributes.Directory | FileAttributes.ReadOnly);

            var hookRan = false;
            void Swap(string path)
            {
                if (!OperatingSystem.IsWindows() || hookRan || !string.Equals(path, leaf, StringComparison.OrdinalIgnoreCase)) return;
                hookRan = true;
                try
                {
                    File.Move(leaf, leaf + ".moved");
                    WindowsDirectoryJunction.Create(leaf, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Held open: the swap was denied, which is the protected outcome.
                }
            }

            try
            {
                NoFollowDirectoryDeleter.Delete(
                    shadowRoot, CancellationToken.None, beforeMutate: Swap, logger: NullLogger.Instance);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A failed delete is acceptable; touching the target is not.
            }

            Assert.IsTrue(hookRan, "The beforeMutate hook must run for the leaf.");
            Assert.IsTrue(File.Exists(targetFile), "The swapped-in target must be untouched.");
            Assert.AreEqual(FileAttributes.ReadOnly, File.GetAttributes(target) & FileAttributes.ReadOnly,
                "The external target directory read-only attribute must not be cleared.");
        }
        finally
        {
            try { File.SetAttributes(Path.Combine(root, "external-target"), FileAttributes.Directory); } catch (IOException) { }
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Dispose_PathBeyondMaxPath_RemovesRoot()
    {
        var root = NewRoot("longpath");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            var deep = shadowRoot;
            for (var i = 0; i < 8; i++)
            {
                deep = Path.Combine(deep, new string((char)('a' + i), 50));
            }

            Assert.IsTrue(deep.Length > 300, "The fixture must exceed MAX_PATH.");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "copy.dll"), "fixture");

            var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                shadowRoot, [], 0, NullLogger.Instance);
            lease.Dispose();

            Assert.IsFalse(Directory.Exists(shadowRoot), "A path longer than MAX_PATH must still be removed.");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [TestMethod]
    public void Dispose_OrdinaryOwnedRoot_IsFullyRemoved()
    {
        var root = NewRoot("ordinary");
        try
        {
            var shadowRoot = Path.Combine(root, "lease");
            Directory.CreateDirectory(Path.Combine(shadowRoot, "a", "b"));
            File.WriteAllText(Path.Combine(shadowRoot, "a", "b", "copy.dll"), "fixture");
            var readOnly = Path.Combine(shadowRoot, "a", "ro.dll");
            File.WriteAllText(readOnly, "fixture");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);

            var lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                shadowRoot, [], 0, NullLogger.Instance);
            lease.Dispose();

            Assert.IsFalse(Directory.Exists(shadowRoot));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string NewRoot(string label)
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-nofollow-" + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root) => TestFixtureFileSystem.DeleteDirectoryIfExists(root);
}
