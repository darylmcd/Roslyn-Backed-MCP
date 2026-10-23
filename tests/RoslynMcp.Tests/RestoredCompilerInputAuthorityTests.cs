using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoredCompilerInputAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectLibraryReceipt_RejectsUnreviewedPathOrMismatchedFramework(bool wrongFramework)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(RestoredCompilerInputAuthorityTests), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "A", "A.csproj");
        var assets = Path.Combine(root, "A", "obj", "project.assets.json");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            foreach (var name in new[]
            {
                "A",
                "B",
                "C"
            }

            )
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(Path.Combine(directory, "obj"));
                var next = name == "A" ? "B" : name == "B" ? "C" : null;
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup>" + (next is null ? "" : "<ItemGroup><ProjectReference Include=\"../" + next + "/" + next + ".csproj\" /></ItemGroup>") + "</Project>");
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".cs"), "public class " + name + " { }");
            }

            var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            await RestoreFixtureAsync(root, project, globals);
            var policy = new OwnedTestWorkspacePathPolicy();
            var beforeCandidate = await PhysicalGraphCandidate.LoadAsync("project-receipt", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? resourceFailure12 = null;
            try
            {
                var before = await RestoredCompilerInputAuthority.CaptureAsync("project-receipt", beforeCandidate.Solution, static path => path, policy, RestoreArtifactPhase.BeforeRestore, CancellationToken.None);
                // A frozen assets receipt alone cannot make an unreviewed compiler context valid.
                var document = JsonNode.Parse(await File.ReadAllBytesAsync(assets))!.AsObject();
                var target = document["targets"]!["net10.0"]!.AsObject();
                var entry = target.Single(item => item.Key.StartsWith("C/", StringComparison.Ordinal));
                Assert.AreEqual("project", entry.Value!["type"]!.GetValue<string>());
                var library = document["libraries"]![entry.Key]!.AsObject();
                Assert.AreEqual("project", library["type"]!.GetValue<string>());
                Assert.IsTrue(document["projectFileDependencyGroups"]!["net10.0"]!.AsArray().Any(item => item!.GetValue<string>().StartsWith("B ", StringComparison.Ordinal)), "B must be the direct dependency and C the actual transitive project receipt.");
                if (wrongFramework)
                    entry.Value!["framework"] = "net9.0";
                else
                    library["msbuildProject"] = "../Unreviewed/Unreviewed.csproj";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
                await File.WriteAllBytesAsync(assets, bytes);
                var afterCandidate = await PhysicalGraphCandidate.LoadAsync("project-receipt-after", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? resourceFailure13 = null;
                try
                {
                    var after = await RestoredCompilerInputAuthority.CaptureAsync("project-receipt", afterCandidate.Solution, static path => path, policy, RestoreArtifactPhase.AfterRestore, CancellationToken.None);
                    await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => before.WithRefreshedInputsAsync("project-receipt", policy, beforeCandidate.Solution, afterCandidate, after, static path => path, static path => path, new HashSet<string>(), new HashSet<string>(), CancellationToken.None));
                    CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(assets));
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure13 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure13, afterCandidate);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure12 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure12, beforeCandidate);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler input fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("assets")]
    [DataRow("props")]
    public async Task FrozenProducerEpoch_RejectsChangedArtifactsAndExplicitSuccessPreservesAuthoredSiblings(string changed)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(RestoredCompilerInputAuthorityTests), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "A.csproj");
        var assets = Path.Combine(root, "obj", "project.assets.json");
        var props = Path.Combine(root, "obj", "A.csproj.nuget.g.props");
        var sibling = Path.Combine(root, "Owned.props");
        const string sentinel = "synthetic-private-compiler-context-73bb";
        var globals = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string> { ["PrivateCompilerAuthorityValue"] = sentinel });
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            await File.WriteAllTextAsync(project, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""");
            await File.WriteAllTextAsync(props, "<Project />");
            await File.WriteAllTextAsync(sibling, "<Project />");
            await File.WriteAllTextAsync(Path.Combine(root, "A.cs"), "public class A { }");
            await RestoreFixtureAsync(root, project, globals);
            var policy = new OwnedTestWorkspacePathPolicy();
            var candidate = await PhysicalGraphCandidate.LoadAsync("artifact-test", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? resourceFailure14 = null;
            try
            {
                var before = await RestoredCompilerInputAuthority.CaptureAsync("artifact-test", candidate.Solution, static path => path, policy, RestoreArtifactPhase.BeforeRestore, CancellationToken.None);
                await before.AssertCurrentAsync("artifact-test", policy, CancellationToken.None);
                var redacted = await Assert.ThrowsExactlyAsync<IOException>(() => before.AssertCurrentAsync("artifact-test", new RejectingPolicy(sentinel), CancellationToken.None));
                Assert.IsFalse(redacted.ToString().Contains(sentinel, StringComparison.Ordinal));
                var authored = await PhysicalInputIdentity.CaptureAsync(sibling, CancellationToken.None);
                await File.AppendAllTextAsync(changed == "assets" ? assets : props, "\n");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => before.AssertCurrentAsync("artifact-test", policy, CancellationToken.None));
                var reloaded = await PhysicalGraphCandidate.LoadAsync("artifact-test-refreshed", project, globals, policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? resourceFailure15 = null;
                try
                {
                    var after = await RestoredCompilerInputAuthority.RefreshAfterSuccessfulRestoreAsync("artifact-test", reloaded, static path => path, policy, finalInvocation: true, CancellationToken.None);
                    var next = await after.AdvanceGeneratedOutputEpochAsync("artifact-test", [..before.Assets, authored], static path => path, new HashSet<string>(), policy, CancellationToken.None);
                    await PhysicalInputIdentity.AssertCurrentAsync(next, CancellationToken.None);
                    await File.AppendAllTextAsync(sibling, "\n");
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => after.AdvanceGeneratedOutputEpochAsync("artifact-test", [..before.Assets, authored], static path => path, new HashSet<string>(), policy, CancellationToken.None));
                }
                catch (Exception cleanupFailure)
                {
                    resourceFailure15 = cleanupFailure;
                    throw;
                }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure15, reloaded);
                }
            }
            catch (Exception cleanupFailure)
            {
                resourceFailure14 = cleanupFailure;
                throw;
            }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Restore authority resource cleanup failed.", resourceFailure14, candidate);
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler input fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static async Task RestoreFixtureAsync(string root, string project, IReadOnlyDictionary<string, string> globals)
    {
        var result = await new DotnetCommandRunner().RunAsync(root, project,
            MsBuildCommandProperties.Append(["restore", project, "--packages", Path.Combine(root, "packages"), "--nologo"], globals),
            CancellationToken.None);
        Assert.IsTrue(result.Succeeded, result.StdErr + result.StdOut);
    }

    private sealed class RejectingPolicy(string value) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) =>
            throw new IOException("Synthetic input failure: " + value, new IOException(value));
    }
}
