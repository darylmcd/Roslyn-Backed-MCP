using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCertificateCurrentGrantTests
{
    [TestMethod]
    [DataRow("current")]
    [DataRow("mandatory-revoked")]
    [DataRow("dependency-denied")]
    public async Task Capture_RenewsCombinedGrantAfterDependencyCallback(string mode)
    {
        await WithFixtureAsync(async root =>
        {
            var project = Path.Combine(root, "Owner.proj");
            var source = Path.Combine(root, "Source.cs");
            var dependency = Path.Combine(root, "Dependency.bin");
            await File.WriteAllTextAsync(source, "class Source { }", TestContext.CancellationToken);
            await File.WriteAllTextAsync(dependency, "opaque artifact identity", TestContext.CancellationToken);
            new XDocument(new XElement("Project", new XElement("ItemGroup",
                new XElement("Compile", new XAttribute("Include", "Source.cs")),
                new XElement("Reference", new XAttribute("Include", "Dependency"), new XElement("HintPath", "Dependency.bin")))))
                .Save(project);
            var owned = new OwnedTestWorkspacePathPolicy();
            var dependencyReached = false;
            var denied = false;
            FileStream? blocker = null;
            AdhocWorkspace? workspace = null;
            Exception? primary = null;
            try
            {
                var policy = new CallbackPolicy(async (workspaceId, paths, ct) =>
                {
                    if (dependencyReached && mode == "mandatory-revoked" && paths.Contains(source, FileSystemPath.Comparer))
                    {
                        denied = true;
                        throw new UnauthorizedAccessException("The mandatory source grant was revoked during dependency approval.");
                    }
                    await owned.ValidateAsync(workspaceId, paths, ct);
                    if (!dependencyReached && paths.Contains(dependency, FileSystemPath.Comparer)
                        && !paths.Contains(source, FileSystemPath.Comparer))
                    {
                        dependencyReached = true;
                        if (mode == "dependency-denied") throw new UnauthorizedAccessException("The optional dependency grant is denied.");
                        if (mode == "mandatory-revoked") blocker = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                });
                Task<NativeCompilerContextCertificate.Epoch> CaptureAsync() => NativeCompilerContextCertificate.Epoch.CaptureAsync(
                    "certificate-grants", project, OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()),
                    policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
                if (mode == "mandatory-revoked")
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () => { await CaptureAsync(); });
                    Assert.IsTrue(denied, "Current mandatory denial must precede the locked content capture.");
                    Assert.IsNotNull(blocker);
                }
                else
                {
                    NativeCompilerContextCertificate.Epoch epoch = await CaptureAsync();
                    if (mode == "current")
                    {
                        Assert.IsInstanceOfType<NativeCompilerContextCertificate.Epoch.CertifiedEpoch>(epoch);
                    }
                    else
                    {
                        workspace = new AdhocWorkspace();
                        NativeCompilerContextCertificate context = await epoch.CompleteAsync("certificate-grants", workspace.CurrentSolution, policy, CancellationToken.None);
                        Assert.AreEqual(NativeCompilerContextAvailability.BuildDependencyGrantRequired, context.Availability);
                    }
                }
                Assert.IsTrue(dependencyReached, "The independent dependency approval must follow mandatory capture.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Certificate blocker cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                    CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
            }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AliasedDocumentFallback_RenewsEarlierDocumentGrantBeforeCapture(bool revoke)
    {
        await WithFixtureAsync(async root =>
        {
            var project = Path.Combine(root, "Owner.proj");
            var first = Path.Combine(root, "First.cs");
            var later = Path.Combine(root, "Later.cs");
            await File.WriteAllTextAsync(first, "class First { }", TestContext.CancellationToken);
            await File.WriteAllTextAsync(later, "class Later { }", TestContext.CancellationToken);
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "Aliased.cs"), "class Aliased { }", TestContext.CancellationToken);
            var alias = Path.Combine(root, "alias");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, target))
                Assert.Inconclusive("Directory links are unavailable in this environment.");
            var aliased = Path.Combine(alias, "Aliased.cs");
            new XDocument(new XElement("Project", new XElement("ItemGroup",
                new[] { first, later, aliased }.Select(path => new XElement("Compile", new XAttribute("Include", path)))))).Save(project);
            var owned = new OwnedTestWorkspacePathPolicy();
            var aliasBatch = false;
            var laterApproved = false;
            var denied = false;
            FileStream? blocker = null;
            AdhocWorkspace? workspace = null;
            Exception? primary = null;
            try
            {
                var policy = new CallbackPolicy(async (workspaceId, paths, ct) =>
                {
                    if (laterApproved && revoke && paths.Contains(first, FileSystemPath.Comparer))
                    {
                        denied = true;
                        throw new UnauthorizedAccessException("The earlier document grant was revoked by later document approval.");
                    }
                    if (paths.Count > 1 && paths.Contains(aliased, FileSystemPath.Comparer)) aliasBatch = true;
                    await owned.ValidateAsync(workspaceId, paths, ct);
                    if (aliasBatch && !laterApproved && paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], later))
                    {
                        laterApproved = true;
                        if (revoke) blocker = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                });
                Task<NativeCompilerContextCertificate.Epoch> CaptureAsync() => NativeCompilerContextCertificate.Epoch.CaptureAsync(
                    "certificate-alias-grants", project, OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()),
                    policy, SupplementalPackageReadAuthority.None, CancellationToken.None);
                if (revoke)
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () => { await CaptureAsync(); });
                    Assert.IsTrue(denied, "Renew the earlier document grant before resolving or opening its locked content.");
                    Assert.IsNotNull(blocker);
                }
                else
                {
                    NativeCompilerContextCertificate.Epoch epoch = await CaptureAsync();
                    workspace = new AdhocWorkspace();
                    NativeCompilerContextCertificate context = await epoch.CompleteAsync("certificate-alias-grants", workspace.CurrentSolution, policy, CancellationToken.None);
                    Assert.AreEqual(NativeCompilerContextAvailability.AliasedBuildInput, context.Availability);
                }
                Assert.IsTrue(aliasBatch, "A real aliased document must trigger the existing degraded fallback.");
                Assert.IsTrue(laterApproved, "The fallback must authorize the later physical document independently.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Aliased document blocker cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                    CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
            }
        });
    }

    private static async Task WithFixtureAsync(Func<string, Task> inspect)
    {
        var lexical = Path.Combine(TestTempRoot.Current, nameof(NativeCertificateCurrentGrantTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(lexical);
            var root = PhysicalPathResolver.Resolve(lexical);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}");
            await inspect(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Certificate grant fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(lexical)));
        }
    }

    private sealed class CallbackPolicy(Func<string, IReadOnlyList<string>, CancellationToken, Task> validate) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct) => validate(workspaceId, paths, ct);
    }

    public TestContext TestContext { get; set; }
}
