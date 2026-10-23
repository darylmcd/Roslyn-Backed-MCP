using System.Runtime.Versioning;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsDirectoryCoordinateAuthorityTests
{
    [TestMethod]
    [DataRow(".")]
    [DataRow(" ")]
    public Task OrdinaryLeafSuffix_SelectsNormalizedDirectoryAcrossConsumers(string suffix) => WithRootAsync(async root =>
    {
        var ordinary = Path.Combine(root, "leaf");
        var literal = Extended(ordinary + suffix);
        Directory.CreateDirectory(ordinary);
        Directory.CreateDirectory(literal);
        await File.WriteAllTextAsync(Path.Combine(ordinary, "ordinary.marker"), "ordinary");
        await File.WriteAllTextAsync(Path.Combine(literal, "literal.marker"), "literal");
        var ordinaryNative = PhysicalPathResolver.OpenInputWindows(ordinary, true, FileShare.ReadWrite | FileShare.Delete);
        Exception? resourceFailure15 = null;
        try
        {
            var requestedNative = PhysicalPathResolver.OpenInputWindows(ordinary + suffix, true, FileShare.ReadWrite | FileShare.Delete);
            Exception? resourceFailure16 = null;
            try
            {
                var literalNative = PhysicalPathResolver.OpenInputWindows(literal, true, FileShare.ReadWrite | FileShare.Delete);
                Exception? resourceFailure17 = null;
                try
                {
                    Assert.IsFalse(ordinaryNative.IsInvalid);
                    Assert.IsFalse(requestedNative.IsInvalid);
                    Assert.IsFalse(literalNative.IsInvalid);
                    Assert.AreEqual(PhysicalDirectoryLease.ReadIdentity(ordinaryNative), PhysicalDirectoryLease.ReadIdentity(requestedNative), "The direct Win32 producer must select the ordinary directory.");
                    Assert.AreNotEqual(PhysicalDirectoryLease.ReadIdentity(ordinaryNative), PhysicalDirectoryLease.ReadIdentity(literalNative), "The explicitly extended name must identify a distinct real directory.");
                    CollectionAssert.AreEquivalent(new[] { "ordinary.marker" }, Directory.GetFiles(ordinary + suffix).Select(Path.GetFileName).ToArray(), "The managed producer must independently select the same normalized directory.");
                    var scope = PhysicalDirectoryScope.OpenRoute(ordinary + suffix);
                    Exception? resourceFailure18 = null;
                    try
                    {
                        var expected = PhysicalDirectoryScope.OpenRoute(ordinary);
                        Exception? resourceFailure19 = null;
                        try
                        {
                            Assert.IsTrue(scope.SameIdentity(expected));
                            CollectionAssert.AreEquivalent(new[] { "ordinary.marker" }, scope.Entries().ToArray());
                            scope.AssertCurrentPath();
                        }
                        catch (Exception failure)
                        {
                            resourceFailure19 = failure;
                            throw;
                        }
                        finally
                        {
                            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure19, expected);
                        }
                    }
                    catch (Exception failure)
                    {
                        resourceFailure18 = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure18, scope);
                    }
                }
                catch (Exception failure)
                {
                    resourceFailure17 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure17, literalNative);
                }
            }
            catch (Exception failure)
            {
                resourceFailure16 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure16, requestedNative);
            }
        }
        catch (Exception failure)
        {
            resourceFailure15 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure15, ordinaryNative);
        }
    });

    [TestMethod]
    [DataRow(".")]
    [DataRow(" ")]
    public Task OrdinaryLeafSuffix_MissingCoordinateMatchesNormalizedAbsence(string suffix) => WithRootAsync(async root =>
    {
        var requested = Path.Combine(root, "absent" + suffix);
        Assert.IsFalse(Directory.Exists(requested));
        Assert.AreEqual(Path.Combine(root, "absent"), Path.GetFullPath(requested));
        var scope = PhysicalDirectoryScope.OpenRouteIfPresent(requested);
        Exception? resourceFailure20 = null;
        try
        {
            Assert.IsNull(scope);
            var identity = await PhysicalInputReader.CaptureIdentityAsync(requested, CancellationToken.None);
            Assert.AreEqual(RoslynMcp.Roslyn.Services.PhysicalInputKind.Missing, identity.Kind);
        }
        catch (Exception failure)
        {
            resourceFailure20 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure20, scope);
        }
    });

    [TestMethod]
    [DataRow("", false)]
    [DataRow("", true)]
    [DataRow(".", true)]
    [DataRow(" ", true)]
    public Task ExplicitPhysicalCoordinate_HasOneObjectAcrossNativeManagedAndHeldReads(string suffix, bool extended) => WithRootAsync(async root =>
    {
        var requested = Path.Combine(root, "leaf" + suffix);
        if (extended)
            requested = Extended(requested);
        Directory.CreateDirectory(requested);
        var file = Path.Combine(requested, "sentinel.marker");
        await File.WriteAllTextAsync(file, "one physical object");
        var native = PhysicalPathResolver.OpenInputWindows(file, false, FileShare.ReadWrite | FileShare.Delete);
        Exception? resourceFailure21 = null;
        try
        {
            Assert.IsFalse(native.IsInvalid, "The independent direct Win32 reader must open the exact authored sentinel.");
            var scope = PhysicalDirectoryScope.OpenRoute(requested);
            Exception? resourceFailure22 = null;
            try
            {
                CollectionAssert.AreEquivalent(new[] { "sentinel.marker" }, scope.Entries().ToArray());
                var held = scope.OpenReadFileIfPresent("sentinel.marker", CancellationToken.None);
                Exception? resourceFailure23 = null;
                try
                {
                    Assert.IsNotNull(held);
                    Assert.AreEqual(PhysicalDirectoryLease.ReadIdentity(native), PhysicalDirectoryLease.ReadIdentity(held));
                    CollectionAssert.AreEqual(await File.ReadAllBytesAsync(file), await PhysicalInputReader.ReadAllBytesAsync(file, CancellationToken.None));
                    scope.AssertCurrentPath();
                }
                catch (Exception failure)
                {
                    resourceFailure23 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure23, held);
                }
            }
            catch (Exception failure)
            {
                resourceFailure22 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure22, scope);
            }
        }
        catch (Exception failure)
        {
            resourceFailure21 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure21, native);
        }
    });

    [TestMethod]
    [DataRow(".", "ordinary.marker")]
    [DataRow(" ", "literal.marker")]
    public Task IntermediateSuffix_PreservesNativeCoordinateAndPinsActualParent(string suffix, string selectedMarker) => WithRootAsync(async root =>
    {
        var ordinaryParent = Path.Combine(root, "leaf");
        var literalParent = Extended(ordinaryParent + suffix);
        var ordinary = Path.Combine(ordinaryParent, "nested");
        var literal = Path.Combine(literalParent, "nested");
        Directory.CreateDirectory(ordinary);
        Directory.CreateDirectory(literal);
        await File.WriteAllTextAsync(Path.Combine(ordinary, "ordinary.marker"), "ordinary");
        await File.WriteAllTextAsync(Path.Combine(literal, "literal.marker"), "literal");
        var requested = Path.Combine(ordinaryParent + suffix, "nested");
        var expected = selectedMarker == "ordinary.marker" ? ordinary : literal;
        var native = PhysicalPathResolver.OpenInputWindows(Path.Combine(requested, selectedMarker), false, FileShare.ReadWrite | FileShare.Delete);
        Exception? resourceFailure24 = null;
        try
        {
            var expectedNative = PhysicalPathResolver.OpenInputWindows(Path.Combine(expected, selectedMarker), false, FileShare.ReadWrite | FileShare.Delete);
            Exception? resourceFailure25 = null;
            try
            {
                Assert.IsFalse(native.IsInvalid);
                Assert.IsFalse(expectedNative.IsInvalid);
                Assert.AreEqual(PhysicalDirectoryLease.ReadIdentity(expectedNative), PhysicalDirectoryLease.ReadIdentity(native), "The actual Win32 producer must independently establish the selected intermediate component.");
                CollectionAssert.AreEquivalent(new[] { selectedMarker }, Directory.GetFiles(requested).Select(Path.GetFileName).ToArray());
                var scope = PhysicalDirectoryScope.OpenRoute(requested);
                Exception? resourceFailure26 = null;
                try
                {
                    CollectionAssert.AreEquivalent(new[] { selectedMarker }, scope.Entries().ToArray());
                    var held = scope.OpenReadFileIfPresent(selectedMarker, CancellationToken.None);
                    Exception? resourceFailure27 = null;
                    try
                    {
                        Assert.IsNotNull(held);
                        Assert.AreEqual(PhysicalDirectoryLease.ReadIdentity(native), PhysicalDirectoryLease.ReadIdentity(held));
                        var selectedParent = selectedMarker == "ordinary.marker" ? Extended(ordinaryParent) : literalParent;
                        var retained = Extended(Path.Combine(root, "renamed-selected-parent"));
                        Assert.ThrowsExactly<IOException>(() => Directory.Move(selectedParent, retained), "The actual selected intermediate parent must stay pinned, not only its leaf or ordinary sibling.");
                        scope.AssertCurrentPath();
                    }
                    catch (Exception failure)
                    {
                        resourceFailure27 = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure27, held);
                    }
                }
                catch (Exception failure)
                {
                    resourceFailure26 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure26, scope);
                }
            }
            catch (Exception failure)
            {
                resourceFailure25 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure25, expectedNative);
            }
        }
        catch (Exception failure)
        {
            resourceFailure24 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure24, native);
        }
    });

    [TestMethod]
    [DataRow(".")]
    [DataRow(" ")]
    public Task ExplicitLiteralComponent_PhysicalReaderPreservesNativeFileIdentity(string suffix) => WithRootAsync(async root =>
    {
        var requested = Extended(Path.Combine(root, "leaf" + suffix));
        Directory.CreateDirectory(requested);
        var file = Path.Combine(requested, "sentinel.marker");
        await File.WriteAllTextAsync(file, "literal component");
        var native = PhysicalPathResolver.OpenInputWindows(file, false, FileShare.ReadWrite | FileShare.Delete);
        Exception? resourceFailure28 = null;
        try
        {
            Assert.IsFalse(native.IsInvalid);
            Assert.AreEqual("literal component", await File.ReadAllTextAsync(file));
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(file), await PhysicalInputReader.ReadAllBytesAsync(file, CancellationToken.None));
        }
        catch (Exception failure)
        {
            resourceFailure28 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure28, native);
        }
    });

    [TestMethod]
    [DataRow(".")]
    [DataRow(" ")]
    public Task ExplicitLiteralComponent_MetadataUsesNativeObjectRatherThanOrdinarySibling(string suffix) => WithRootAsync(async root =>
    {
        var ordinary = Path.Combine(root, "leaf");
        var literal = Extended(ordinary + suffix);
        Directory.CreateDirectory(ordinary);
        Directory.CreateDirectory(literal);
        var ordinaryFile = Path.Combine(ordinary, "sentinel.marker");
        var literalFile = Path.Combine(literal, "sentinel.marker");
        await File.WriteAllTextAsync(ordinaryFile, "ordinary");
        await File.WriteAllTextAsync(literalFile, "literal");
        File.SetLastWriteTimeUtc(ordinaryFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(literalFile, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var native = PhysicalPathResolver.OpenInputWindows(literalFile, true, FileShare.ReadWrite | FileShare.Delete);
        Exception? resourceFailure29 = null;
        try
        {
            var sibling = PhysicalPathResolver.OpenInputWindows(ordinaryFile, true, FileShare.ReadWrite | FileShare.Delete);
            Exception? resourceFailure30 = null;
            try
            {
                Assert.IsFalse(native.IsInvalid);
                Assert.IsFalse(sibling.IsInvalid);
                var expected = File.GetLastWriteTimeUtc(native);
                Assert.AreNotEqual(expected, File.GetLastWriteTimeUtc(sibling));
                Assert.AreEqual(expected, File.GetLastWriteTimeUtc(literalFile));
                Assert.AreEqual(expected, PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(literalFile, CancellationToken.None));
            }
            catch (Exception failure)
            {
                resourceFailure30 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure30, sibling);
            }
        }
        catch (Exception failure)
        {
            resourceFailure29 = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure29, native);
        }
    });

    private static string Extended(string path) => @"\\?\" + path;

    private static async Task WithRootAsync(Func<string, Task> body)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Actual Windows pathname semantics are required.");
        var root = Path.Combine(TestTempRoot.Current, "windows-coordinate-" + Guid.NewGuid().ToString("N"));
        if (root.Length < 3 || root[1] != ':') Assert.Inconclusive("This native spelling control requires an actual drive-letter fixture.");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await body(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Windows coordinate fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
