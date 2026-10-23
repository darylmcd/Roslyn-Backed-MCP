using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerShadowSweepAuthorityTests
{
    [TestMethod]
    public async Task Sweep_LinkedOwnerMarkerDeclinesCleanupWithoutEscaping()
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-owner-link-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared");
        var process = Path.Combine(shared, "process-" + Guid.NewGuid().ToString("N"));
        var lease = Path.Combine(process, "stale-lease");
        var external = Path.Combine(root, "external");
        var marker = Path.Combine(process, ".owner.lock");
        var sentinel = Path.Combine(external, "preserve.txt");
        var owned = Path.Combine(lease, "owned.txt");
        var old = DateTime.UtcNow.AddDays(-3);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(lease);
            Directory.CreateDirectory(external);
            await File.WriteAllTextAsync(sentinel, "external bytes");
            await File.WriteAllTextAsync(owned, "owned bytes");
            Directory.SetCreationTimeUtc(lease, old);
            Directory.SetLastWriteTimeUtc(lease, old);
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(marker, external);
            else Directory.CreateSymbolicLink(marker, external);
            Assert.IsTrue((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0);
            Assert.AreEqual("external bytes", await File.ReadAllTextAsync(Path.Combine(marker, "preserve.txt")),
                "The owner-marker alias must actually resolve to the external directory.");

            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);

            Assert.AreEqual("external bytes", await File.ReadAllTextAsync(sentinel));
            Assert.AreEqual("owned bytes", await File.ReadAllTextAsync(owned),
                "An unavailable owner marker must prevent reclamation of that process's leases.");
            Assert.IsTrue((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Linked analyzer marker fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => RemoveAliasIfPresent(marker)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sweep_WorkspaceJunctionCannotDeleteExternalStaleChildren(bool replaceOwnedDirectory)
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-sweep-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared");
        var external = Path.Combine(root, "external");
        var link = Path.Combine(shared, "workspace");
        var old = DateTime.UtcNow.AddDays(-3);
        var staleChild = Path.Combine(external, "stale-child");
        var sentinel = Path.Combine(staleChild, "preserve.txt");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(shared);
            Directory.CreateDirectory(staleChild);
            await File.WriteAllTextAsync(sentinel, "external bytes");
            Directory.SetCreationTimeUtc(staleChild, old);
            Directory.SetLastWriteTimeUtc(staleChild, old);
            if (replaceOwnedDirectory)
            {
                Directory.CreateDirectory(link);
                await File.WriteAllTextAsync(Path.Combine(link, "owned.txt"), "owned bytes");
                Directory.Move(link, Path.Combine(root, "retained-owned-directory"));
            }
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(link, external);
            else Directory.CreateSymbolicLink(link, external);
            Assert.IsTrue((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            Assert.IsTrue(File.Exists(Path.Combine(link, "stale-child", "preserve.txt")), "The real junction must resolve to the external sentinel.");
            AnalyzerReferenceIsolation.SweepAbandonedRoots(shared, NullLogger.Instance);
            Assert.IsTrue(File.Exists(sentinel), "Enumerating a workspace junction must not authorize deletion of its target's children.");
            Assert.AreEqual("external bytes", await File.ReadAllTextAsync(sentinel));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer workspace alias fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => RemoveAliasIfPresent(link)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task OwnedDirectoryScope_ParentReplacementCannotRedirectDeletion()
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-scope-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "shared");
        var external = Path.Combine(root, "external");
        var sentinel = Path.Combine(external, "preserve.txt");
        PhysicalDirectoryScope? scope = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(shared);
            Directory.CreateDirectory(external);
            await File.WriteAllTextAsync(sentinel, "external bytes");
            await File.WriteAllTextAsync(Path.Combine(shared, "owned.txt"), "owned bytes");
            scope = PhysicalDirectoryScope.OpenRoute(shared);
            {
                var moved = false;
                try { Directory.Move(shared, Path.Combine(root, "retained")); moved = true; }
                catch (IOException) when (OperatingSystem.IsWindows()) { }
                if (moved)
                {
                    if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(shared, external);
                    else Directory.CreateSymbolicLink(shared, external);
                    Assert.IsTrue(File.Exists(Path.Combine(shared, "preserve.txt")), "The replacement must actually resolve externally.");
                }
                scope.DeleteChildren(CancellationToken.None);
                Assert.IsTrue(File.Exists(sentinel));
                Assert.AreEqual("external bytes", await File.ReadAllTextAsync(sentinel));
                Assert.IsFalse(File.Exists(Path.Combine(root, moved ? "retained" : "shared", "owned.txt")),
                    "Deletion must still act on the owned directory identity.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer parent replacement fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => scope?.Dispose()),
                CleanupFailureCollector.FromAction(() => RemoveAliasIfPresent(shared)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LeaseCleanup_AncestorReplacementCannotDeleteExternalChildren(bool replaceOwnRoot)
    {
        var root = Path.Combine(TestTempRoot.Current, "shadow-lease-cleanup-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "parent");
        var shadow = Path.Combine(parent, "lease");
        var external = Path.Combine(root, "external");
        var sentinel = Path.Combine(external, "lease", "preserve.txt");
        PhysicalDirectoryScope.OwnedRoot? shadowOwner = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? lease = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(parent);
            shadowOwner = PhysicalDirectoryScope.CreateOwnedRoot(shadow);
            Directory.CreateDirectory(Path.Combine(external, "lease"));
            await File.WriteAllTextAsync(sentinel, "external bytes");
            await File.WriteAllTextAsync(Path.Combine(shadow, "owned.txt"), "owned bytes");
            var relocated = false;
            lease = new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(shadowOwner, [], 0, NullLogger.Instance);
            shadowOwner = null;
            {
                var blocked = false;
                try { Directory.Move(replaceOwnRoot ? shadow : parent, Path.Combine(root, "retained")); relocated = true; }
                catch (IOException) when (OperatingSystem.IsWindows()) { blocked = true; }
                if (!blocked)
                {
                    var alias = replaceOwnRoot ? shadow : parent;
                    var target = replaceOwnRoot ? Path.Combine(external, "lease") : external;
                    if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(alias, target);
                    else Directory.CreateSymbolicLink(alias, target);
                    Assert.IsTrue(File.Exists(Path.Combine(parent, "lease", "preserve.txt")),
                        "An unpinned ancestor must actually redirect to the external sentinel before cleanup.");
                }
            }
            var completedLease = lease;
            lease = null;
            completedLease.Dispose();
            Assert.IsTrue(File.Exists(sentinel));
            Assert.AreEqual("external bytes", await File.ReadAllTextAsync(sentinel));
            var original = relocated
                ? replaceOwnRoot ? Path.Combine(root, "retained") : Path.Combine(root, "retained", "lease") : shadow;
            Assert.IsFalse(File.Exists(Path.Combine(original, "owned.txt")), "Known owned bytes must be removed from the captured identity.");
            if (relocated && replaceOwnRoot && !OperatingSystem.IsWindows())
            {
                // POSIX can retain the open directory identity across rename, but cannot
                // unlink it by descriptor after its original name was replaced. Cleanup
                // must leave the empty moved directory rather than discover a new name.
                Assert.IsTrue(Directory.Exists(original));
                Assert.IsFalse(Directory.EnumerateFileSystemEntries(original).Any());
                Assert.IsTrue((File.GetAttributes(shadow) & FileAttributes.ReparsePoint) != 0);
            }
            else if (OperatingSystem.IsWindows()) Assert.IsFalse(Directory.Exists(original), "A stable Windows owned root must be removed.");
            else
            {
                Assert.IsTrue(Directory.Exists(original), "POSIX retains the original final directory name.");
                Assert.HasCount(0, Directory.GetFileSystemEntries(original));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var alias = replaceOwnRoot ? shadow : parent;
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer lease ancestor fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => shadowOwner?.Dispose()),
                CleanupFailureCollector.FromAction(() => RemoveAliasIfPresent(alias)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task ConfiguredTemporaryBootstrap_OnlyConfiguredPrefixMayFollowAnAlias()
    {
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        Console.WriteLine("ConfiguredTempAlias=" + !FileSystemPath.Comparer.Equals(temporary,
            Path.TrimEndingDirectorySeparator(PhysicalPathResolver.Resolve(temporary))));
        var root = Path.Combine(TestTempRoot.Current, "shadow-bootstrap-" + Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "owned");
        var external = Path.Combine(root, "external");
        var linkedShared = Path.Combine(root, "linked-shared");
        var sentinel = Path.Combine(external, "stale-child", "preserve.txt");
        var old = DateTime.UtcNow.AddDays(-3);
        PhysicalDirectoryScope.OwnedRoot? scope = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            scope = PhysicalDirectoryScope.CreateOwnedRoot(owned);
            Directory.CreateDirectory(Path.Combine(external, "stale-child"));
            await File.WriteAllTextAsync(Path.Combine(owned, "owned.txt"), "owned bytes");
            await File.WriteAllTextAsync(sentinel, "external bytes");
            Directory.SetCreationTimeUtc(Path.GetDirectoryName(sentinel)!, old);
            Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(sentinel)!, old);
            Assert.IsNotNull(scope);
            scope.Delete(CancellationToken.None);
            scope.Dispose();
            scope = null;
            Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(owned), "Final POSIX names are retained; Windows removes the owned object.");
            Assert.IsFalse(File.Exists(Path.Combine(owned, "owned.txt")), "Configured-prefix cleanup must reclaim the actual owned payload.");
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(linkedShared, external);
            else Directory.CreateSymbolicLink(linkedShared, external);
            Assert.IsTrue(File.Exists(Path.Combine(linkedShared, "stale-child", "preserve.txt")));
            AnalyzerReferenceIsolation.SweepAbandonedRoots(linkedShared, NullLogger.Instance);
            Assert.IsTrue(File.Exists(sentinel), "A link below the configured bootstrap is never an owned shared root.");
            Assert.AreEqual("external bytes", await File.ReadAllTextAsync(sentinel));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Analyzer configured bootstrap fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => scope?.Dispose()),
                CleanupFailureCollector.FromAction(() => RemoveAliasIfPresent(linkedShared)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static void RemoveAliasIfPresent(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            Directory.Delete(path);
    }
}
