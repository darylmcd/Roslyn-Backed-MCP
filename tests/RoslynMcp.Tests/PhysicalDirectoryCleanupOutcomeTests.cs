using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalDirectoryCleanupOutcomeTests
{
    [TestMethod]
    public async Task OwnedCleanup_ReportsPlatformOutcomeAndNeverReentersCompletedNamespace()
    {
        var fixture = NewFixture();
        var issued = Path.Combine(fixture, "issued");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            var owned = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            Exception? resourceFailure1 = null;
            try
            {
                var nested = Path.Combine(issued, "nested");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, "owned.txt"), "owned bytes");
                var result = owned.Delete(CancellationToken.None);
                Assert.AreEqual(Expected, result);
                if (OperatingSystem.IsWindows())
                    Assert.IsFalse(Directory.Exists(issued));
                else
                {
                    Assert.IsTrue(Directory.Exists(nested));
                    Assert.HasCount(0, Directory.GetFiles(nested));
                }

                Directory.CreateDirectory(issued);
                var later = Path.Combine(issued, "later.txt");
                File.WriteAllText(later, "later unrelated bytes");
                Assert.AreEqual(result, owned.Delete(CancellationToken.None));
                Assert.AreEqual("later unrelated bytes", File.ReadAllText(later));
            }
            catch (Exception failure)
            {
                resourceFailure1 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure1, owned);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await Clean(fixture, primary);
        }
    }

    [TestMethod]
    public async Task ContentsCleanup_PropagatesRetainedDescendantAndPreservesRoot()
    {
        var fixture = NewFixture();
        var issued = Path.Combine(fixture, "issued");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(Path.Combine(issued, "nested"));
            File.WriteAllText(Path.Combine(issued, "nested", "owned.txt"), "owned bytes");
            var scope = PhysicalDirectoryScope.OpenRoute(issued);
            Exception? resourceFailure2 = null;
            try
            {
                Assert.IsTrue(scope.HasReclaimableContent(CancellationToken.None));
                Assert.AreEqual(Expected, scope.DeleteChildren(CancellationToken.None));
                Assert.IsFalse(scope.HasReclaimableContent(CancellationToken.None), "Retained nested directory names alone are not payload.");
                Assert.IsTrue(Directory.Exists(issued));
                Assert.IsFalse(File.Exists(Path.Combine(issued, "nested", "owned.txt")));
                Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(Path.Combine(issued, "nested")));
            }
            catch (Exception failure)
            {
                resourceFailure2 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure2, scope);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await Clean(fixture, primary);
        }
    }

    [TestMethod]
    public async Task PlatformRemoval_OriginalObjectStaysBoundThroughFinalOperation()
    {
        var fixture = NewFixture();
        var issued = Path.Combine(fixture, "issued");
        var retained = Path.Combine(fixture, "retained");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(issued);
            if (OperatingSystem.IsWindows())
            {
                var pin = PhysicalDirectoryLease.OpenWindows(issued);
                IDisposable? pinFallback = pin;
                Exception? resourceFailure3 = null;
                try
                {
                    var witness = PhysicalDirectoryLease.CaptureDeletionWitness(issued);
                    Exception? resourceFailure4 = null;
                    try
                    {
                        pinFallback = null;
                        pin.Dispose();
                        {
                            var removal = witness.BindIssuedName(issued, CancellationToken.None);
                            Exception? resourceFailure5 = null;
                            try
                            {
                                Assert.ThrowsExactly<IOException>(() => Directory.Move(issued, retained));
                                Assert.IsTrue(Directory.Exists(issued));
                                removal.Delete(CancellationToken.None);
                            }
                            catch (Exception failure)
                            {
                                resourceFailure5 = failure;
                                throw;
                            }
                            finally
                            {
                                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure5, removal);
                            }
                        }

                        Assert.IsFalse(Directory.Exists(issued));
                    }
                    catch (Exception failure)
                    {
                        resourceFailure4 = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure4, witness);
                    }
                }
                catch (Exception failure)
                {
                    resourceFailure3 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure3, pinFallback);
                }
            }
            else
            {
                var parent = PhysicalDirectoryScope.OpenRoute(fixture);
                Exception? resourceFailure6 = null;
                try
                {
                    var child = parent.OpenChild("issued");
                    Exception? resourceFailure7 = null;
                    try
                    {
                        Assert.IsNotNull(child);
                        Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Retained, parent.RemoveHeldChild("issued", child, CancellationToken.None));
                        Directory.Move(issued, retained);
                        Directory.CreateDirectory(issued);
                        Assert.ThrowsExactly<IOException>(() => parent.RemoveHeldChild("issued", child, CancellationToken.None));
                        Assert.IsTrue(Directory.Exists(issued));
                        Assert.IsTrue(Directory.Exists(retained));
                    }
                    catch (Exception failure)
                    {
                        resourceFailure7 = failure;
                        throw;
                    }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure7, child);
                    }
                }
                catch (Exception failure)
                {
                    resourceFailure6 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure6, parent);
                }
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await Clean(fixture, primary);
        }
    }

    [TestMethod]
    public async Task PlatformRemoval_CancellationPreservesOriginalForSuccessfulRetry()
    {
        var fixture = NewFixture();
        var issued = Path.Combine(fixture, "issued");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(fixture);
            var owned = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            Exception? resourceFailure8 = null;
            try
            {
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                Assert.ThrowsExactly<OperationCanceledException>(() => owned.Delete(canceled.Token));
                Assert.IsTrue(Directory.Exists(issued));
                Assert.AreEqual(Expected, owned.Delete(CancellationToken.None));
            }
            catch (Exception failure)
            {
                resourceFailure8 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure8, owned);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await Clean(fixture, primary);
        }
    }

    private static PhysicalDirectoryCleanupOutcome Expected => OperatingSystem.IsWindows()
        ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained;
    private static string NewFixture()
    {
        var path = Path.Combine(TestTempRoot.Current, "directory-outcome-" + Guid.NewGuid().ToString("N"));
        return path;
    }
    private static ValueTask Clean(string fixture, Exception? primary) => CleanupFailureCollector.RunAfterFailureAsync(
        "Directory outcome regression and cleanup failed.", primary,
        CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
}
