using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RestoreCurrentReadAuthorityTests
{
    [TestMethod]
    public void Locator_PreCanceledTokenStopsBeforeNativeDiscovery()
    {
        var cancellation = new CancellationTokenSource();
        Exception? primary = null;
        try
        {
            cancellation.Cancel();
            var observed = Assert.ThrowsExactly<OperationCanceledException>(() => RestoreArtifactLocator.Find(
                Path.Combine(TestTempRoot.Current, "must-not-evaluate", "Missing.csproj"), ImmutableDictionary<string, string>.Empty, cancellation.Token));
            Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Restore locator cancellation cleanup failed.", primary, cancellation); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CurrentAssetsDenial_EscapesBeforePollingOrDriftRead(bool drift)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(RestoreCurrentReadAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        AdhocWorkspace? workspace = null;
        FileStream? locked = null;
        try
        {
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "Owner.csproj");
            var assets = Path.Combine(root, "assets.json");
            await File.WriteAllTextAsync(projectPath, new System.Xml.Linq.XElement("Project",
                new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                    new System.Xml.Linq.XElement("ProjectAssetsFile", assets)),
                new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("PackageReference",
                    new System.Xml.Linq.XAttribute("Include", "OwnedFixture"), new System.Xml.Linq.XAttribute("Version", "1.0.0")))).ToString());
            await File.WriteAllTextAsync(assets, "invalid bytes must not be observed after denial");
            var expected = new UnauthorizedAccessException("Current client denies the selecting assets.");
            var checks = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (paths.Contains(assets, FileSystemPath.Comparer)) { checks++; throw expected; }
                return new OwnedTestWorkspacePathPolicy([root]).ValidateAsync("restore-read", paths, ct);
            }
            var detector = new RestoreStalenessDetector();
            workspace = new AdhocWorkspace();
            var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: projectPath));
            locked = new FileStream(assets, FileMode.Open, FileAccess.Read, FileShare.None);
            var observed = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () =>
            {
                if (drift) _ = await detector.DetectRestoreRequiredAsync(project.Solution, Authorize, NullLogger.Instance, CancellationToken.None);
                else await detector.WaitForStableRestoreArtifactsAsync(projectPath, ImmutableDictionary<string, string>.Empty,
                    Authorize, 1, NullLogger.Instance, CancellationToken.None);
            });
            Assert.AreSame(expected, observed);
            Assert.AreEqual(1, checks);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Restore current-read fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => locked?.Dispose()),
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
