using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeTaskRegistryUnavailableTests
{
    [TestMethod]
    [DataRow("semantic")]
    [DataRow("entry-drift")]
    [DataRow("denied")]
    public async Task RegistryCapabilityUnavailable_BindsEntryAndRetainsSemanticOnlyAuthority(string shape)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeTaskRegistryUnavailableTests), Guid.NewGuid().ToString("N"));
        MSBuildWorkspace? workspace = null;
        IDisposable? lease = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "Semantic.csproj");
            await File.WriteAllTextAsync(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
            await File.WriteAllTextAsync(Path.Combine(root, "Semantic.cs"), "public class Semantic { public int Value => 7; }");
            var properties = OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>());
            var policy = new OwnedTestWorkspacePathPolicy();
            if (shape == "denied")
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => NativeCompilerContextCertificate.Epoch.RegistryUnavailableAsync(
                    "registry-unavailable", path, properties, new DeniedPolicy(), CancellationToken.None));
                return;
            }
            var epoch = await NativeCompilerContextCertificate.Epoch.RegistryUnavailableAsync("registry-unavailable", path, properties, policy, CancellationToken.None);
            if (shape == "entry-drift")
            {
                await File.AppendAllTextAsync(path, "\n<!-- changed -->");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => epoch.AssertPreparationCurrentAsync("registry-unavailable", policy, CancellationToken.None));
                return;
            }
            workspace = MSBuildWorkspace.Create(properties);
            var project = await workspace.OpenProjectAsync(path);
            Assert.IsTrue(project.Documents.Any(document => document.Name == "Semantic.cs"));
            var certificate = await epoch.CompleteAsync("registry-unavailable", workspace.CurrentSolution, policy, CancellationToken.None);
            Assert.AreEqual(NativeCompilerContextAvailability.TaskRegistryUnavailable, certificate.Availability);
            await certificate.PrepareProjectionAsync("registry-unavailable", policy, CancellationToken.None);
            lease = await certificate.InstallAnalyzerIsolationAsync("registry-unavailable", policy, NullLogger.Instance, CancellationToken.None);
            var projection = await certificate.NormalizeAsync("registry-unavailable", policy, CancellationToken.None);
            var refusal = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(projection.Solution));
            StringAssert.Contains(refusal.Message, "supported SDK");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Unavailable registry fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => lease?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class DeniedPolicy : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
            => throw new UnauthorizedAccessException("Synthetic current entry denial.");
    }
}
