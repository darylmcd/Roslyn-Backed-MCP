using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeMandatorySourceAuthorityTests
{
    [TestMethod]
    [DataRow("invalid-assets", false)]
    [DataRow("invalid-assets", true)]
    [DataRow("missing-assets", false)]
    [DataRow("missing-assets", true)]
    [DataRow("cache-grant", false)]
    [DataRow("cache-grant", true)]
    public async Task DeniedCompileSource_RemainsMandatoryAcrossUnavailableAuthorityStates(string state, bool external)
    {
        var sidecar = Path.Combine(TestTempRoot.Current, "denied-compile-" + Guid.NewGuid().ToString("N"));
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Dependency", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/_._"] = [] });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var source = Path.Combine(external ? sidecar : fixture.Root, "Denied.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(source) ?? throw new AssertFailedException("The source fixture needs a directory."));
            byte[] original = "public class Denied { }"u8.ToArray();
            await File.WriteAllBytesAsync(source, original);
            if (external)
            {
                var xml = XDocument.Load(project);
                Assert.IsNotNull(xml.Root);
                xml.Root.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", source))));
                await File.WriteAllTextAsync(project, xml.ToString());
            }
            if (state == "invalid-assets") await File.WriteAllTextAsync(fixture.Assets, "{invalid-assets");
            else if (state == "missing-assets") File.Delete(fixture.Assets);
            var policy = new MandatorySourcePolicy(source, state == "cache-grant" ? fixture.Cache : null);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), watcher,
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None));
            StringAssert.Contains(failure.Message, "Owned Compile input denied");
            Assert.IsTrue(policy.SourceDenials > 0, "An unavailable dependency epoch must not bypass mandatory Compile authorization.");
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Mandatory source fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(sidecar)));
        }
    }

    private sealed class MandatorySourcePolicy(string source, string? cache) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _inner = new();
        internal int SourceDenials { get; private set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (paths.Contains(source, FileSystemPath.Comparer))
            {
                SourceDenials++;
                throw new UnauthorizedAccessException("Owned Compile input denied.");
            }
            if (cache is not null && paths.Any(path => FileSystemPath.Comparer.Equals(path, cache)
                || FileSystemPath.IsStrictDescendant(cache, path)))
                throw new UnauthorizedAccessException("The recorded cache is outside this fixture grant.");
            return _inner.ValidateAsync(workspaceId, paths, ct);
        }
    }
}
