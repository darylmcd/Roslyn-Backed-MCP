using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspacePathPolicyTests
{
    [TestMethod]
    public async Task IssuedRefusalMarker_PreservesTypeAndCannotBeForgedWithMessageOrData()
    {
        var root = Path.Combine(ClientRootPathValidator.ResolvePath(TestTempRoot.Current), Guid.NewGuid().ToString("N"));
        var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [Path.Combine(root, "Allowed")] });
        var issued = await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync("fixture",
            [Path.Combine(root, "Denied.cs")], CancellationToken.None));
        Assert.IsTrue(ClientRootPathValidator.IsSanctionedRootBoundaryRefusal(issued));
        Assert.IsTrue(WorkspacePathPolicyRefusal.IsDenied(issued));
        var forged = new ArgumentException(issued.Message, issued.ParamName);
        foreach (System.Collections.DictionaryEntry entry in issued.Data) forged.Data[entry.Key] = entry.Value;
        Assert.IsFalse(WorkspacePathPolicyRefusal.IsDenied(forged));
        Assert.IsFalse(ClientRootPathValidator.IsSanctionedRootBoundaryRefusal(forged));
    }

    [TestMethod]
    public void RedactedIssuedRefusal_PreservesTypedDenialWithoutExposingAuthoredValues()
    {
        const string value = "private-build-property-value";
        var issued = WorkspacePathPolicyRefusal.Mark(new PublicArgumentException("Denied " + value, "paths"),
            WorkspacePathPolicyRefusalKind.AliasedPath);
        var redactor = new MsBuildCommandProperties.Redactor(["-p:PrivateValue=" + value]);
        var sanitized = redactor.Failure(issued);
        Assert.AreEqual(typeof(PublicArgumentException), sanitized.GetType());
        Assert.AreNotSame(issued, sanitized);
        Assert.IsFalse(sanitized.ToString().Contains(value, StringComparison.Ordinal));
        Assert.IsTrue(WorkspacePathPolicyRefusal.Is(sanitized, WorkspacePathPolicyRefusalKind.AliasedPath));
        Assert.IsFalse(WorkspacePathPolicyRefusal.IsDenied(new ArgumentException(sanitized.Message)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PhysicalDirectory_TrailingSeparatorDoesNotCreateLinkIdentity(bool exists)
    {
        var root = ClientRootPathValidator.ResolvePath(TestTempRoot.Current);
        var directory = Path.Combine(root, "policy-directory-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            if (exists) Directory.CreateDirectory(directory);
            var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [root] });
            await policy.ValidateAsync("fixture", [directory + Path.DirectorySeparatorChar], CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Policy directory fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (Directory.Exists(directory)) Directory.Delete(directory); }));
        }
    }

    [TestMethod]
    public async Task CompleteInventory_RefusesOutsideSiblingAlongsideAllowedSource()
    {
        var root = Path.Combine(ClientRootPathValidator.ResolvePath(TestTempRoot.Current), Guid.NewGuid().ToString("N"));
        var allowed = Path.Combine(root, "Allowed");
        var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [allowed] });
        await policy.ValidateAsync("fixture", [Path.Combine(allowed, "Source.cs")], CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync("fixture",
            [Path.Combine(allowed, "Source.cs"), Path.Combine(root, "Sibling", "Sibling.csproj")], CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Expansion_RequiresBothOperatorPermissionAndWorkspaceGrant(bool operatorPermission, bool workspaceGrant)
    {
        var root = Path.Combine(ClientRootPathValidator.ResolvePath(TestTempRoot.Current), Guid.NewGuid().ToString("N"));
        var workspaceId = Guid.NewGuid().ToString("N");
        var policy = new WorkspacePathPolicy(new SecurityOptions
        {
            SanctionedRoots = [Path.Combine(root, "Allowed")],
            AllowRootExpansion = operatorPermission
        });
        if (workspaceGrant) RootExpansionGrantRegistry.Grant(workspaceId);
        try
        {
            var paths = new[] { Path.Combine(root, "Sibling", "Source.cs") };
            if (operatorPermission && workspaceGrant)
                await policy.ValidateAsync(workspaceId, paths, CancellationToken.None);
            else
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync(workspaceId, paths, CancellationToken.None));
        }
        finally { RootExpansionGrantRegistry.Revoke(workspaceId); }
    }

    [TestMethod]
    public async Task MissingConfiguredRoots_RefusesInventory()
    {
        var policy = new WorkspacePathPolicy(new SecurityOptions());
        await Assert.ThrowsExactlyAsync<PublicArgumentException>(() => policy.ValidateAsync("fixture",
            [Path.Combine(TestTempRoot.Current, "Source.cs")], CancellationToken.None));
    }

    [TestMethod]
    public async Task CurrentMcpRequestRoots_NarrowTheCompleteInventoryWithoutLeakingAcrossScopes()
    {
        var root = Path.Combine(ClientRootPathValidator.ResolvePath(TestTempRoot.Current), "policy-request-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root, "First");
        var second = Path.Combine(root, "Second");
        var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [root] });
        McpRootsTestServerFactory.Session? firstRequest = null;
        McpRootsTestServerFactory.Session? secondRequest = null;
        Exception? primary = null;
        try
        {
            firstRequest = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(root,
                CancellationToken.None, clientRootPaths: [first]);
            secondRequest = await McpRootsTestServerFactory.CreateWithSanctionedRootAsync(root,
                CancellationToken.None, clientRootPaths: [second]);
            var firstPath = Path.Combine(first, "Source.cs");
            var secondPath = Path.Combine(second, "Destination.cs");
            using (RequestMcpServerContext.Begin(firstRequest.Server))
            {
                await policy.ValidateAsync("fixture", [firstPath], CancellationToken.None);
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync("fixture", [firstPath, secondPath], CancellationToken.None));
                using (RequestMcpServerContext.Begin(secondRequest.Server))
                {
                    await policy.ValidateAsync("fixture", [secondPath], CancellationToken.None);
                    await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync("fixture", [firstPath], CancellationToken.None));
                }
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => policy.ValidateAsync("fixture", [secondPath], CancellationToken.None));
            }
            await policy.ValidateAsync("fixture", [firstPath, secondPath], CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("MCP request roots fixture cleanup failed.", primary,
                async () => { if (secondRequest is not null) await secondRequest.DisposeAsync(); },
                async () => { if (firstRequest is not null) await firstRequest.DisposeAsync(); });
        }
    }

    [TestMethod]
    public async Task AllowedLinkedAncestor_RequiresItsPhysicalTargetForExistingAndNewLeaves()
    {
        var root = Path.Combine(ClientRootPathValidator.ResolvePath(TestTempRoot.Current), "policy-physical-" + Guid.NewGuid().ToString("N"));
        var physical = Path.Combine(root, "Physical");
        var alias = Path.Combine(root, "Alias");
        var source = Path.Combine(physical, "Source.cs");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(physical);
            await File.WriteAllTextAsync(source, "// original source");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, physical))
            {
                Assert.Inconclusive("Directory links are unavailable in this test environment.");
                return;
            }
            var policy = new WorkspacePathPolicy(new SecurityOptions { SanctionedRoots = [root] });
            await policy.ValidateAsync("fixture", [source, Path.Combine(physical, "New.cs")], CancellationToken.None);
            foreach (var leaf in new[] { "Source.cs", "New.cs" })
            {
                var error = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
                    policy.ValidateAsync("fixture", [source, Path.Combine(alias, leaf)], CancellationToken.None));
                Assert.AreEqual("paths", error.ParamName);
                StringAssert.Contains(error.PublicMessage, "physical canonical paths");
            }
            Assert.AreEqual("// original source", await File.ReadAllTextAsync(source));
            Assert.IsFalse(File.Exists(Path.Combine(physical, "New.cs")));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Linked policy fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
