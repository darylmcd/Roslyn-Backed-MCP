using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalDirectoryRetryAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OwnedCleanup_PreservesAlreadyPresentEmptyReplacement(bool replaceAncestor)
    {
        var fixture = Path.Combine(TestTempRoot.Current, "empty-cleanup-replacement-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var issued = Path.Combine(parent, "issued");
        var retained = Path.Combine(fixture, "retained");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(parent);
            var owned = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            Exception? resourceFailure9 = null;
            try
            {
                File.WriteAllText(Path.Combine(issued, "owned.txt"), "owned bytes");
                var moved = false;
                try
                {
                    Directory.Move(replaceAncestor ? parent : issued, retained);
                    moved = true;
                }
                catch (IOException) when (OperatingSystem.IsWindows())
                {
                }

                if (!moved)
                {
                    owned.Delete(CancellationToken.None);
                    Assert.IsFalse(Directory.Exists(issued));
                    return;
                }

                Directory.CreateDirectory(issued);
                Assert.AreEqual(0, Directory.GetFileSystemEntries(issued).Length);
                var original = replaceAncestor ? Path.Combine(retained, "issued") : retained;
                if (replaceAncestor)
                    owned.Delete(CancellationToken.None);
                else
                    Assert.ThrowsExactly<IOException>(() => owned.Delete(CancellationToken.None));
                Assert.IsFalse(File.Exists(Path.Combine(original, "owned.txt")));
                Assert.IsTrue(Directory.Exists(issued), "An empty replacement is still an unrelated directory.");
                Assert.AreEqual(0, Directory.GetFileSystemEntries(issued).Length);
            }
            catch (Exception failure)
            {
                resourceFailure9 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure9, owned);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory retry fixture cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    [TestMethod]
    public async Task ExpiredForkSweep_LinkedRootCannotGrantExternalCleanupAuthority()
    {
        var root = Path.Combine(TestTempRoot.Current, "expired-fork-authority-" + Guid.NewGuid().ToString("N"));
        var external = Path.Combine(root, "external");
        var forkRoot = Path.Combine(root, "forks");
        var now = DateTimeOffset.UtcNow;
        var expired = Path.Combine(external, now.AddDays(-3).ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture) + "-external");
        var sentinel = Path.Combine(expired, "preserve.txt");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(expired);
            await File.WriteAllTextAsync(sentinel, "external expired-directory bytes");
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(forkRoot, external);
            else Directory.CreateSymbolicLink(forkRoot, external);
            Assert.AreEqual("external expired-directory bytes", await File.ReadAllTextAsync(Path.Combine(forkRoot, Path.GetFileName(expired), "preserve.txt")));

            WorkspaceForkApplyService.SweepExpiredForks(forkRoot, 24, now, NullLogger.Instance);

            Assert.IsTrue(File.Exists(sentinel), "Timestamp selection beneath an unowned link does not grant cleanup authority.");
            Assert.AreEqual("external expired-directory bytes", await File.ReadAllTextAsync(sentinel));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Linked fork sweep fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() =>
                {
                    if (Directory.Exists(forkRoot) && (File.GetAttributes(forkRoot) & FileAttributes.ReparsePoint) != 0) Directory.Delete(forkRoot);
                }),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void Sweep_WorkspaceRemovalPreservesReplacementAfterLeaseCleanup()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "sweep-workspace-retry-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(fixture, "shared");
        var workspace = Path.Combine(shared, "workspace");
        var lease = Path.Combine(workspace, "lease");
        var retained = Path.Combine(shared, "retained");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(lease);
            File.WriteAllText(Path.Combine(lease, "owned.txt"), "owned bytes");
            var old = DateTime.UtcNow.AddDays(-3);
            Directory.SetCreationTimeUtc(lease, old);
            Directory.SetLastWriteTimeUtc(lease, old);
            var logger = new ReplacementLogger(() =>
            {
                try { Directory.Move(workspace, retained); }
                catch (IOException) when (OperatingSystem.IsWindows()) { return false; }
                Directory.CreateDirectory(workspace);
                return true;
            });
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, logger);
            Assert.IsTrue(logger.Invoked, "The stale lease must actually be reclaimed before replacing its workspace name.");
            if (logger.Replaced)
            {
                Assert.IsTrue(Directory.Exists(workspace), "Workspace removal must retain its original identity after lease cleanup.");
                Assert.AreEqual(0, Directory.GetFileSystemEntries(workspace).Length);
                Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(Path.Combine(retained, "lease")));
                Assert.IsFalse(File.Exists(Path.Combine(retained, "lease", "owned.txt")));
            }
            else Assert.IsFalse(Directory.Exists(workspace), "A pinned namespace must still permit normal full cleanup.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Directory retry fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture))); }
    }

    [TestMethod]
    public void OwnedCleanup_FailedUnlinkOrRetainedCompletionPreservesEmptyReplacement()
    {
        var fixture = Path.Combine(TestTempRoot.Current, "cleanup-retry-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var issued = Path.Combine(parent, "issued");
        var retained = Path.Combine(parent, "retained");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(parent);
            var owned = PhysicalDirectoryScope.CreateOwnedRoot(issued);
            Exception? resourceFailure10 = null;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    {
                        var cleanupResource11 = PhysicalDirectoryScope.OpenRoute(issued);
                        Exception? resourceFailure11 = null;
                        try
                        {
                            Assert.ThrowsExactly<IOException>(() => owned.Delete(CancellationToken.None), "A separate no-delete root handle must prevent the first nonrecursive unlink.");
                        }
                        catch (Exception failure)
                        {
                            resourceFailure11 = failure;
                            throw;
                        }
                        finally
                        {
                            OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure11, cleanupResource11);
                        }
                    }
                }
                else
                {
                    Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Retained, owned.Delete(CancellationToken.None), "POSIX must retain final names instead of attempting a permission-sensitive name unlink.");
                }

                Assert.IsTrue(Directory.Exists(issued), "The first failed unlink must leave the original root present.");
                Directory.Move(issued, retained);
                Directory.CreateDirectory(issued);
                Assert.AreEqual(0, Directory.GetFileSystemEntries(issued).Length, "The replacement must be an ordinary empty directory before retry.");
                Assert.IsFalse((File.GetAttributes(issued) & FileAttributes.ReparsePoint) != 0);
                if (OperatingSystem.IsWindows())
                    Assert.ThrowsExactly<IOException>(() => owned.Delete(CancellationToken.None), "Every failed Windows removal retry must compare the live original identity.");
                else
                    Assert.AreEqual(PhysicalDirectoryCleanupOutcome.Retained, owned.Delete(CancellationToken.None), "A terminal retained outcome never reenters an unrelated replacement.");
                Assert.IsTrue(Directory.Exists(issued), "An unlink failure does not grant authority over a later empty replacement.");
                Assert.IsTrue(Directory.Exists(retained));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(issued).Length);
            }
            catch (Exception failure)
            {
                resourceFailure10 = failure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure10, owned);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Directory retry fixture cleanup failed.", primary, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    private sealed class ReplacementLogger(Func<bool> replace) : ILogger
    {
        internal bool Invoked { get; private set; }
        internal bool Replaced { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (Invoked || message is not ("Swept one abandoned analyzer shadow lease."
                or "Reclaimed abandoned analyzer shadow content; retained directory namespace.")) return;
            Invoked = true;
            Replaced = replace();
        }
    }
}
