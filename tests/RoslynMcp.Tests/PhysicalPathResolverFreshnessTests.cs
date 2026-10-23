using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalPathResolverFreshnessTests
{
    [TestMethod]
    public void ExistingAndMissingOrdinaryPaths_PreservePhysicalCoordinates()
    {
        var root = Path.Combine(TestTempRoot.Current, "resolver-ordinary-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "nested");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "input.cs");
            File.WriteAllText(file, "class Input { }");
            var physicalRoot = PhysicalPathResolver.Resolve(root);
            Assert.AreEqual(Path.Combine(physicalRoot, "nested"), PhysicalPathResolver.Resolve(directory));
            Assert.AreEqual(Path.Combine(physicalRoot, "nested"), PhysicalPathResolver.Resolve(directory + Path.DirectorySeparatorChar));
            Assert.AreEqual(Path.Combine(physicalRoot, "nested", "input.cs"), PhysicalPathResolver.Resolve(file));
            Assert.AreEqual(Path.Combine(physicalRoot, "nested", "absent", "new.cs"),
                PhysicalPathResolver.Resolve(Path.Combine(directory, "absent", "new.cs")));
            Assert.AreEqual(Path.Combine(physicalRoot, "nested", "input.cs"),
                PhysicalPathResolver.Resolve(Path.Join(directory, ".", "input.cs")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void ExistingLinkedTarget_AndItsParentTraversal_UsePhysicalCoordinates()
    {
        var root = Path.Combine(TestTempRoot.Current, "resolver-existing-link-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(root, "link");
        var outside = Path.Combine(root, "outside");
        var target = Path.Combine(outside, "target");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "input.cs"), "class Input { }");
            File.WriteAllText(Path.Combine(outside, "parent.cs"), "class Parent { }");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            Assert.AreEqual(PhysicalPathResolver.Resolve(target), PhysicalPathResolver.Resolve(link));
            Assert.AreEqual(PhysicalPathResolver.Resolve(Path.Combine(target, "input.cs")),
                PhysicalPathResolver.Resolve(Path.Combine(link, "input.cs")));
            Assert.AreEqual(PhysicalPathResolver.Resolve(Path.Combine(outside, "parent.cs")),
                PhysicalPathResolver.Resolve(Path.Join(link, "..", "parent.cs")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void ReplacedOrdinaryComponent_IsResolvedAgainBeforeParentTraversal()
    {
        var root = Path.Combine(TestTempRoot.Current, "resolver-freshness-" + Guid.NewGuid().ToString("N"));
        var component = Path.Combine(root, "component");
        var outside = Path.Combine(root, "outside");
        var target = Path.Combine(outside, "target");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(component);
            Directory.CreateDirectory(target);
            var traversed = Path.Join(component, "..", "missing.cs");
            Assert.AreEqual(PhysicalPathResolver.Resolve(Path.Combine(root, "missing.cs")), PhysicalPathResolver.Resolve(traversed));
            Directory.Delete(component);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(component, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            Assert.AreEqual(PhysicalPathResolver.Resolve(Path.Combine(outside, "missing.cs")), PhysicalPathResolver.Resolve(traversed),
                "A previously ordinary component must be inspected again before resolving its parent.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void MissingComponent_IsNotCachedAndLaterLinkIsResolved()
    {
        var root = Path.Combine(TestTempRoot.Current, "resolver-missing-" + Guid.NewGuid().ToString("N"));
        var component = Path.Combine(root, "component");
        var target = Path.Combine(root, "target");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(target);
            Assert.AreEqual(Path.Combine(PhysicalPathResolver.Resolve(root), "component", "new.cs"),
                PhysicalPathResolver.Resolve(Path.Combine(component, "new.cs")));
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(component, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            Assert.AreEqual(PhysicalPathResolver.Resolve(Path.Combine(target, "new.cs")),
                PhysicalPathResolver.Resolve(Path.Combine(component, "new.cs")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
