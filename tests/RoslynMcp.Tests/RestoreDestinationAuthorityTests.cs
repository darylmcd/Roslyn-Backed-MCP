using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoreDestinationAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CustomDestination_RejectsPhysicalAliasAfterCurrentGrant(bool parentTraversal)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(RestoreDestinationAuthorityTests), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "outside", "target");
        var link = Path.Combine(root, "link");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(target);
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(link, target))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }

            var destination = parentTraversal ? Path.Join(link, "..", "cache") : link;
            var grants = 0;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => BuildInputProvenance.AuthorizeRestoreDestinationsAsync([destination], (paths, _) =>
            {
                CollectionAssert.AreEqual(new[] { destination }, paths.ToArray());
                grants++;
                return Task.CompletedTask;
            }, CancellationToken.None));
            Assert.AreEqual(1, grants, "Physical checks must follow current custom-root authorization.");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "outside", "cache")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "cache")));
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Restore destination fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task ConfiguredCacheRoot_IsRestoreDestinationButNotInstalledFileAuthority()
    {
        MsBuildInitializer.EnsureInitialized();
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Assert.IsTrue(BuildInputProvenance.IsBootstrapRestoreDestination(root));
        Assert.IsTrue(BuildInputProvenance.IsBootstrapRestoreDestination(root + Path.DirectorySeparatorChar));
        Assert.IsFalse(BuildInputProvenance.IsBootstrapPackagePath(root));
        Assert.IsFalse(BuildInputProvenance.IsBootstrapRestoreDestination(Path.GetDirectoryName(root)!));
        Assert.IsFalse(BuildInputProvenance.IsBootstrapRestoreDestination(root + "-unapproved"));
        await BuildInputProvenance.AuthorizeRestoreDestinationsAsync([root], (paths, _) =>
        {
            Assert.IsEmpty(paths, "The independently configured cache root needs no additional workspace grant.");
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task CustomDestination_RechecksCurrentGrantBeforeEveryRestore()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(RestoreDestinationAuthorityTests), Guid.NewGuid().ToString("N"));
        var allowed = true;
        var calls = 0;
        Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CollectionAssert.AreEqual(new[] { root }, paths.ToArray());
            calls++;
            if (!allowed) throw new UnauthorizedAccessException("Synthetic revoked restore destination.");
            return Task.CompletedTask;
        }
        await BuildInputProvenance.AuthorizeRestoreDestinationsAsync([root, root], Authorize, CancellationToken.None);
        allowed = false;
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            BuildInputProvenance.AuthorizeRestoreDestinationsAsync([root], Authorize, CancellationToken.None));
        Assert.AreEqual(2, calls);
        Assert.IsFalse(Directory.Exists(root), "Authorization must not create a destination.");
    }
}
