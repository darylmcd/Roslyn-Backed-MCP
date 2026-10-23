using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalPathResolverHandleFreshnessTests
{
    [TestMethod]
    public async Task ExistingPath_ReplacedByLinkedAncestor_ResolvesAgainAndRefusesEscape()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalPathResolverHandleFreshnessTests), Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "allowed");
        var component = Path.Combine(allowed, "component");
        var target = Path.Combine(root, "outside");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(component);
            Directory.CreateDirectory(target);
            var path = Path.Combine(component, "input.cs");
            var targetFile = Path.Combine(target, "input.cs");
            File.WriteAllText(path, "class Before { }");
            File.WriteAllText(targetFile, "class After { }");
            Assert.AreEqual(Path.Combine(PhysicalPathResolver.Resolve(allowed), "component", "input.cs"), PhysicalPathResolver.Resolve(path));
            File.Delete(path);
            Directory.Delete(component);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(component, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            Assert.AreEqual(PhysicalPathResolver.Resolve(targetFile), PhysicalPathResolver.Resolve(path));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ClientRootPathValidator.ValidatePathAgainstRootsAsync(server: null, path, CancellationToken.None,
                    securityOptions: new SecurityOptions { SanctionedRoots = [allowed] }));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void ExistingFile_ThroughSubdirectoryDriveAlias_UsesBackingPhysicalCoordinates()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Drive-letter aliases are a Windows filesystem concept.");
            return;
        }
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalPathResolverHandleFreshnessTests), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        WindowsDriveAlias? alias = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(target);
            var path = Path.Combine(target, "input.cs");
            File.WriteAllText(path, "class Input { }");
            alias = WindowsDriveAlias.Create(target);
            var aliasFile = Path.Combine(alias.RootPath, "input.cs");
            Assert.IsTrue(File.Exists(aliasFile));
            Assert.AreEqual(PhysicalPathResolver.Resolve(path), PhysicalPathResolver.Resolve(aliasFile));
            Assert.AreNotEqual(Path.GetFullPath(aliasFile), PhysicalPathResolver.Resolve(aliasFile));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical path freshness fixture cleanup failed.", primary, alias,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
