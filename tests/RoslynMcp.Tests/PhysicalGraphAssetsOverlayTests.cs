using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphAssetsOverlayTests
{
    [TestMethod]
    public async Task OwnedPackageVersionOverlay_RetainsBothInstallationsWithDistinctSelectingEpochs()
    {
        const string id = "Owned.VersionOverlay";
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        PhysicalGraphStage? stage = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync(id, "1.0.0",
                new Dictionary<string, byte[]> { ["tools/Marker.txt"] = Encoding.UTF8.GetBytes("first installation") });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var originalProject = await File.ReadAllBytesAsync(project);
            var obj = Path.Combine(fixture.Root, "obj");
            var originalOutputs = new Dictionary<string, byte[]>(FileSystemPath.Comparer);
            foreach (var path in Directory.GetFiles(obj, "*", SearchOption.AllDirectories))
                originalOutputs.Add(path, await File.ReadAllBytesAsync(path));
            var feed = Path.Combine(fixture.Root, "feed");
            var nuspec = new XElement("package", new XElement("metadata", new XElement("id", id),
                new XElement("version", "2.0.0"), new XElement("authors", "Fixture"), new XElement("description", "Owned replacement installation")));
            await NuGetInstallationFixture.WriteArchiveAsync(Path.Combine(feed, id + ".2.0.0.nupkg"), new Dictionary<string, byte[]>
            {
                [id + ".nuspec"] = Encoding.UTF8.GetBytes(nuspec.ToString()),
                ["tools/Marker.txt"] = Encoding.UTF8.GetBytes("second installation")
            });
            var xml = XDocument.Load(project);
            xml.Descendants("PackageReference").Single().SetAttributeValue("Version", "2.0.0");
            await File.WriteAllTextAsync(project, xml.ToString());
            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo", "--source", feed, "--packages", fixture.Cache], fixture.LoadProperties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The desired package selection must come from a successful real NuGet restore.");
            var desiredFiles = new List<FileSnapshotDto> { FileSnapshotDto.FromExistingBytes(project, await File.ReadAllBytesAsync(project)) };
            foreach (var path in Directory.GetFiles(obj, "*", SearchOption.AllDirectories))
            {
                desiredFiles.Add(FileSnapshotDto.FromExistingBytes(path, await File.ReadAllBytesAsync(path)));
                if (!originalOutputs.ContainsKey(path)) File.Delete(path);
            }
            foreach (var (path, bytes) in originalOutputs) await File.WriteAllBytesAsync(path, bytes);
            await File.WriteAllBytesAsync(project, originalProject);

            var grants = new RevocablePolicy();
            var policy = new ForkInvocationPathPolicy(grants);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            var source = manager.GetCurrentSolution(status.WorkspaceId);
            var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, project, source, policy, CancellationToken.None);
            var first = Path.Combine(fixture.Cache, id.ToLowerInvariant(), "1.0.0", "tools", "Marker.txt");
            var second = Path.Combine(fixture.Cache, id.ToLowerInvariant(), "2.0.0", "tools", "Marker.txt");
            Assert.IsTrue(layout.Packages.Files.Contains(first, FileSystemPath.Comparer));
            Assert.IsFalse(layout.Packages.Files.Contains(second, FileSystemPath.Comparer), "The source receipt must select only the original installation.");
            stage = await PhysicalGraphStage.CreateAsync(status.WorkspaceId, project, source, layout,
                desiredFiles, fixture.LoadProperties, new WorkspaceSessionLoader(), policy,
                ((IWorkspaceEvaluationLoader)manager).CaptureStageInvocationAuthority(status.WorkspaceId, source), NullLogger.Instance, CancellationToken.None);
            stage.EnterReadAuthority();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken token) => policy.ValidateAsync(status.WorkspaceId, paths, token);
            var privateProof = stage.Expected.Packages.MapSelectingAssets(stage.MapFromSource);
            await stage.Layout.Packages.AssertCurrentAsync(Authorize, CancellationToken.None);
            await privateProof.AssertCurrentAsync(Authorize, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Expected.Packages.AssertCurrentAsync(Authorize, CancellationToken.None));
            foreach (var member in new[] { first, second })
            {
                Assert.IsTrue(stage.Layout.Packages.Files.Contains(member, FileSystemPath.Comparer));
                Assert.IsTrue(stage.Expected.Packages.Files.Contains(member, FileSystemPath.Comparer));
                var bytes = await File.ReadAllBytesAsync(member);
                Exception? memberPrimary = null;
                try
                {
                    await File.WriteAllTextAsync(member, "tampered installation");
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Layout.Packages.AssertCurrentAsync(Authorize, CancellationToken.None));
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => privateProof.AssertCurrentAsync(Authorize, CancellationToken.None));
                }
                catch (Exception failure) { memberPrimary = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Package overlay member restoration failed.", memberPrimary,
                        () => new ValueTask(File.WriteAllBytesAsync(member, bytes)));
                }
                grants.DeniedPath = member;
                try
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => stage.Layout.Packages.AssertCurrentAsync(Authorize, CancellationToken.None));
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => privateProof.AssertCurrentAsync(Authorize, CancellationToken.None));
                }
                finally { grants.DeniedPath = null; }
            }
            CollectionAssert.AreEqual(originalProject, await File.ReadAllBytesAsync(project));
            CollectionAssert.AreEqual(originalOutputs[fixture.Assets], await File.ReadAllBytesAsync(fixture.Assets));
            await privateProof.AssertCurrentAsync(Authorize, CancellationToken.None);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Package overlay fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => stage?.Dispose()),
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task OwnedAssetsOverlay_KeepsSourceAndDesiredPackageReceiptsInTheirOwnEpochs()
    {
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        FileWatcherService? watcher = null;
        PhysicalGraphStage? stage = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.AssetsOverlay", "1.0.0",
                new Dictionary<string, byte[]> { ["tools/Marker.txt"] = Encoding.UTF8.GetBytes("owned package member") });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var grants = new RevocablePolicy();
            var policy = new ForkInvocationPathPolicy(grants);
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(policy, NullLogger<WorkspaceManager>.Instance, new PreviewStore(),
                watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
            var source = manager.GetCurrentSolution(status.WorkspaceId);
            var original = await File.ReadAllBytesAsync(fixture.Assets);
            var desired = original.Concat(Encoding.UTF8.GetBytes("\n")).ToArray();
            var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, project, source, policy, CancellationToken.None);
            stage = await PhysicalGraphStage.CreateAsync(status.WorkspaceId, project, source, layout,
                [FileSnapshotDto.FromExistingBytes(fixture.Assets, desired)], fixture.LoadProperties,
                new WorkspaceSessionLoader(), policy,
                ((IWorkspaceEvaluationLoader)manager).CaptureStageInvocationAuthority(status.WorkspaceId, source), NullLogger.Instance, CancellationToken.None);
            stage.EnterReadAuthority();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken token) => policy.ValidateAsync(status.WorkspaceId, paths, token);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(fixture.Assets), "Private recovery must not publish desired assets into the source.");
            await stage.Layout.Packages.AssertCurrentAsync(Authorize, CancellationToken.None);
            var privateAssets = stage.MapFromSource(fixture.Assets);
            CollectionAssert.AreEqual(desired, await File.ReadAllBytesAsync(privateAssets));
            var privateProof = stage.Expected.Packages.MapSelectingAssets(stage.MapFromSource);
            await privateProof.AssertCurrentAsync(Authorize, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Expected.Packages.AssertCurrentAsync(Authorize, CancellationToken.None));

            Exception? privatePrimary = null;
            try
            {
                await File.WriteAllBytesAsync(privateAssets, original);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => privateProof.AssertCurrentAsync(Authorize, CancellationToken.None));
            }
            catch (Exception failure) { privatePrimary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Private selecting assets restoration failed.", privatePrimary,
                    () => new ValueTask(File.WriteAllBytesAsync(privateAssets, desired)));
            }
            await privateProof.AssertCurrentAsync(Authorize, CancellationToken.None);

            var member = stage.Expected.Packages.Files.Single(path => path.EndsWith("Marker.txt", StringComparison.Ordinal));
            grants.DeniedPath = member;
            try
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => privateProof.AssertCurrentAsync(Authorize, CancellationToken.None));
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => stage.ReplayAuthority.AssertCurrentAsync(status.WorkspaceId, policy, CancellationToken.None));
            }
            finally { grants.DeniedPath = null; }
            Exception? sourcePrimary = null;
            try
            {
                await File.WriteAllBytesAsync(fixture.Assets, desired);
                await stage.Expected.Packages.AssertCurrentAsync(Authorize, CancellationToken.None);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Layout.Packages.AssertCurrentAsync(Authorize, CancellationToken.None));
            }
            catch (Exception failure) { sourcePrimary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Source selecting assets restoration failed.", sourcePrimary,
                    () => new ValueTask(File.WriteAllBytesAsync(fixture.Assets, original)));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Package overlay fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => stage?.Dispose()),
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    private sealed class RevocablePolicy : IWorkspacePathPolicy
    {
        internal string? DeniedPath { get; set; }
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (DeniedPath is { } denied && paths.Contains(denied, FileSystemPath.Comparer))
                throw new UnauthorizedAccessException("The package member grant was revoked.");
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }
}
