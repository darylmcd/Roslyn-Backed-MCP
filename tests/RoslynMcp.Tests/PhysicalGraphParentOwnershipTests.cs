using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphParentOwnershipTests
{
    [TestMethod]
    [DataRow("staging", false)]
    [DataRow("staging", true)]
    [DataRow("forks", false)]
    [DataRow("forks", true)]
    public async Task FinalParentGrant_RefusesAncestorSwapBeforeCreatingSharedParents(string role, bool ancestor)
    {
        var fixture = Path.Combine(TestTempRoot.Current, nameof(PhysicalGraphParentOwnershipTests), Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(fixture, "parent");
        var source = Path.Combine(parent, "source");
        var foreignParent = Path.Combine(fixture, "foreign");
        var foreign = Path.Combine(foreignParent, "source");
        var sentinel = Path.Combine(foreign, "preserve.txt");
        var blocked = false;
        var swapped = false;
        var calls = 0;
        var policy = new CallbackPolicy(() =>
        {
            if (++calls != 2)
                return;
            try
            {
                Directory.Move(ancestor ? parent : source, Path.Combine(fixture, "retained"));
            }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                blocked = true;
                return;
            }

            Directory.Move(ancestor ? foreignParent : foreign, ancestor ? parent : source);
            swapped = true;
        });
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(foreign);
            await File.WriteAllTextAsync(sentinel, "foreign namespace");
            PhysicalDirectoryScope? issuedParent = null;
            Exception? refusal = null;
            try
            {
                issuedParent = await PhysicalGraphDirectory.OpenParentAsync(source, [".roslynmcp", role], "issued", (paths, token) => policy.ValidateAsync("owned-parent", paths, token), CancellationToken.None);
            }
            catch (IOException failure)
            {
                refusal = failure;
            }

            {
                var cleanupResource14 = issuedParent;
                Exception? resourceFailure14 = null;
                try
                {
                    Assert.IsTrue(blocked || swapped, "The real final-authorization swap must execute or be blocked by a lifetime pin.");
                    if (swapped)
                    {
                        Assert.IsNotNull(refusal);
                        Assert.IsFalse(Directory.Exists(Path.Combine(source, ".roslynmcp")), "No shared parent may be created through the replacement route.");
                    }
                    else
                        Assert.IsNotNull(issuedParent, "The Windows pin must allow the original authorized route to proceed.");
                    Assert.AreEqual("foreign namespace", await File.ReadAllTextAsync(Path.Combine(swapped ? source : foreign, "preserve.txt")));
                }
                catch (Exception failure)
                {
                    resourceFailure14 = failure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Directory authority resource cleanup failed.", resourceFailure14, cleanupResource14);
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
            await CleanupFailureCollector.RunAfterFailureAsync("Shared graph parent fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fixture)));
        }
    }

    private sealed class CallbackPolicy(Action callback) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            callback();
            return Task.CompletedTask;
        }
    }
}
