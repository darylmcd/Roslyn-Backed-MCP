using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoreBoundaryRegressionTests
{
    [TestInitialize]
    public void Initialize() => MsBuildInitializer.EnsureInitialized();
    [TestMethod]
    [DataRow("ProjectAssetsFile")]
    [DataRow("ContentPreprocessorOutputDirectory")]
    [DataRow("TargetPath")]
    public void ConsumedPhysicalProperty_RelocatesWithoutDiscoveryHeuristics(string property)
    {
        var original = Path.Combine(TestTempRoot.Current, "source", "outputs", "authority");
        var expected = Path.Combine(TestTempRoot.Current, "fork", "outputs", "authority");
        Assert.AreEqual(expected, BuildPathRelocation.RelocateGlobal(property, original,
            value => value == original ? expected : value, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("locked")]
    [DataRow("shape")]
    [DataRow("wrong-property")]
    public void AuthoritativeCapture_UnreadableExistingAssetsRefusesInsteadOfDroppingRecordedRoot(string kind)
    {
        var root = Path.Combine(TestTempRoot.Current, "RestoreAuthority", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "App.csproj");
        var assets = Path.Combine(root, "project.assets.json");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(project, "<Project><PropertyGroup><ProjectAssetsFile>project.assets.json</ProjectAssetsFile></PropertyGroup></Project>");
            File.WriteAllText(assets, kind switch
            {
                "malformed" => "{",
                "shape" => "[]",
                "wrong-property" => "{\"project\":{\"restore\":{\"packagesPath\":42}}}",
                _ => "{\"project\":{\"restore\":{\"packagesPath\":\"recorded-cache\"}}}"
            });
            var locked = kind == "locked" ? new FileStream(assets, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
            Exception? resourceFailure1 = null;
            try
            {
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => RestoreInvocationPlanner.Capture([project], ImmutableDictionary<string, string>.Empty, CancellationToken.None));
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure1 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure1, locked);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore boundary fixture cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ForkCleanup_DanglingLinkAndCycleAreRemovedWithoutFollowing(bool cycle)
    {
        var root = Path.Combine(TestTempRoot.Current, "CleanupLinkState", Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "owned");
        var target = cycle ? owned : Path.Combine(root, "temporary-target");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(owned);
            Directory.CreateDirectory(target);
            var link = cycle ? Path.Combine(owned, "cycle") : Path.Combine(root, "dangling");
            if (OperatingSystem.IsWindows())
                WindowsDirectoryJunction.Create(link, target);
            else
                Directory.CreateSymbolicLink(link, target);
            if (!cycle)
                Directory.Delete(target);
            var selected = cycle ? owned : link;
            var originalDirectory = cycle && !OperatingSystem.IsWindows() ? PhysicalDirectoryScope.OpenRoute(owned) : null;
            Exception? resourceFailure2 = null;
            try
            {
                var parent = PhysicalDirectoryScope.OpenRoute(Path.GetDirectoryName(selected)!);
                Exception? resourceFailure3 = null;
                try
                {
                    var outcome = parent.DeleteChild(Path.GetFileName(selected), directoriesOnly: true, CancellationToken.None);
                    AssertCleanupResult(selected, outcome, originalDirectory);
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure3 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure3, parent);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure2 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure2, originalDirectory);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore boundary fixture cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void ForkCleanup_CancelledEmptyDirectoryKeepsExactTokenAndContents()
    {
        var root = Path.Combine(TestTempRoot.Current, "CleanupCancellation", Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var parent = PhysicalDirectoryScope.OpenRoute(Path.GetDirectoryName(root)!);
            Exception? resourceFailure4 = null;
            try
            {
                var failure = Assert.ThrowsExactly<OperationCanceledException>(() => parent.DeleteChild(Path.GetFileName(root), directoriesOnly: true, cancellation.Token));
                Assert.AreEqual(cancellation.Token, failure.CancellationToken);
                Assert.IsTrue(Directory.Exists(root));
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure4 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure4, parent);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore boundary fixture cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ForkCleanup_LinkNeverChangesExternalReadonlyTarget(bool rootIsLink)
    {
        var root = Path.Combine(TestTempRoot.Current, "CleanupBoundary", Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "owned");
        var external = Path.Combine(root, "external");
        var marker = Path.Combine(external, "readonly.txt");
        var link = rootIsLink ? owned : Path.Combine(owned, "link");
        FileAttributes? originalAttributes = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(external);
            File.WriteAllText(marker, "external-state");
            originalAttributes = File.GetAttributes(marker);
            File.SetAttributes(marker, originalAttributes.Value | FileAttributes.ReadOnly);
            if (!rootIsLink)
                Directory.CreateDirectory(owned);
            if (OperatingSystem.IsWindows())
                WindowsDirectoryJunction.Create(link, external);
            else
                Directory.CreateSymbolicLink(link, external);
            var originalDirectory = !rootIsLink && !OperatingSystem.IsWindows() ? PhysicalDirectoryScope.OpenRoute(owned) : null;
            Exception? resourceFailure5 = null;
            try
            {
                var parent = PhysicalDirectoryScope.OpenRoute(Path.GetDirectoryName(owned)!);
                Exception? resourceFailure6 = null;
                try
                {
                    var outcome = parent.DeleteChild(Path.GetFileName(owned), directoriesOnly: true, CancellationToken.None);
                    AssertCleanupResult(owned, outcome, originalDirectory);
                    Assert.IsTrue(File.Exists(marker));
                    Assert.IsTrue((File.GetAttributes(marker) & FileAttributes.ReadOnly) != 0, "Cleanup may remove its link, but must not alter attributes through it.");
                    Assert.AreEqual("external-state", File.ReadAllText(marker));
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure6 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure6, parent);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure5 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure5, originalDirectory);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Readonly cleanup fixture release failed.", primary, OwnedResourceCleanup.FromAction(() =>
            {
                if (Directory.Exists(link))
                    Directory.Delete(link);
            }), OwnedResourceCleanup.FromAction(() =>
            {
                if (originalAttributes is { } attributes && File.Exists(marker))
                    File.SetAttributes(marker, attributes);
            }), OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static void AssertCleanupResult(string path, PhysicalDirectoryCleanupOutcome outcome, PhysicalDirectoryScope? originalDirectory)
    {
        if (originalDirectory is null)
        {
            Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Removed, outcome);
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Contains(path, FileSystemPath.Comparer));
            return;
        }

        Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Retained, outcome);
        var retained = PhysicalDirectoryScope.OpenRoute(path);
        Exception? resourceFailure7 = null;
        try
        {
            Assert.IsTrue(originalDirectory.SameIdentity(retained), "POSIX cleanup must retain the same owned directory object.");
            Assert.HasCount(0, retained.Entries(), "The retained directory must contain no links or files.");
        }
        catch (Exception cleanupFailure)
        {
            resourceFailure7 = cleanupFailure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure7, retained);
        }
    }
}
