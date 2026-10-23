using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DownloadedPackageAuthorityTests
{
    private const string PackageId = "Owned.DownloadOnly";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MalformedInstallationMetadata_RefusesWithSanitizedPublicContract(bool downloadOnly)
    {
        var fixture = downloadOnly ? await CreateDownloadAsync()
            : await NuGetInstallationFixture.CreateAsync(PackageId, "1.0.0",
                new Dictionary<string, byte[]> { ["tools/Marker.txt"] = "verified package"u8.ToArray() });
        Exception? usingCleanupPrimary5 = null;
        try
        {
            var policy = new OwnedTestWorkspacePathPolicy();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => policy.ValidateAsync("metadata-shape", paths, ct);
            var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None);
            var metadata = Path.Combine(proof.Installations(PackageId + "/1.0.0").Single(), ".nupkg.metadata");
            var original = await File.ReadAllBytesAsync(metadata);
            foreach (var malformed in new[] { "null", "[]", "\"private-installation-metadata\"", "42", "true",
                "{}", "{\"contentHash\":[]}", "{\"contentHash\":\"private-installation-metadata\"}", "{" })
            {
                await File.WriteAllTextAsync(metadata, malformed);
                var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None), malformed);
                Assert.IsFalse(failure.ToString().Contains("private-installation-metadata", StringComparison.Ordinal));
            }
            await File.WriteAllBytesAsync(metadata, original);
            await proof.AssertCurrentAsync(Authorize, CancellationToken.None);

        }
        catch (Exception failure) { usingCleanupPrimary5 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary5, fixture); }
    }

    [TestMethod]
    [DataRow("download", "..")]
    [DataRow("library", "..")]
    [DataRow("download", ".")]
    [DataRow("library", ".")]
    [DataRow("download", "../Outside")]
    [DataRow("library", "../Outside")]
    public async Task InvalidPackageIdentity_CannotSelectAnInstallationOutsideItsRecordedCache(string role, string id)
    {
        var root = Path.Combine(TestTempRoot.Current, "escaping-download-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var cache = Path.Combine(root, "cache");
            var candidate = new JsonObject { ["packageFolders"] = new JsonObject { [cache] = new JsonObject() } };
            if (role == "library")
                candidate["libraries"] = new JsonObject
                {
                    [id + "/1.0.0"] = new JsonObject
                    { ["type"] = "package", ["path"] = id + "/1.0.0", ["sha512"] = "untrusted", ["files"] = new JsonArray() }
                };
            else
                candidate["project"] = new JsonObject
                {
                    ["frameworks"] = new JsonObject
                    {
                        ["net10.0"] = new JsonObject
                        { ["downloadDependencies"] = new JsonArray(new JsonObject { ["name"] = id, ["version"] = "[1.0.0, 1.0.0]" }) }
                    }
                };
            var assets = Path.Combine(root, "project.assets.json");
            await File.WriteAllTextAsync(assets, candidate.ToJsonString());
            var approvedPaths = new List<string>();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                approvedPaths.AddRange(paths);
                return Task.CompletedTask;
            }
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => InstalledPackageAuthority.CreateAsync(
                assets, File.ReadAllBytes(assets), [], Authorize, CancellationToken.None));
            Assert.IsTrue(approvedPaths.All(path => FileSystemPath.Comparer.Equals(path, assets)),
                "Even an approving policy must never be asked to authorize an installation selected by an escaping package identity.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native authority fixture directory cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("{\"project\":null}")]
    [DataRow("{\"project\":[]}")]
    [DataRow("{\"project\":\"private-invalid-project\"}")]
    [DataRow("{\"project\":{\"frameworks\":null}}")]
    [DataRow("{\"project\":{\"frameworks\":[]}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"private-target\":null}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"private-target\":[]}}}")]
    [DataRow("{\"project\":{\"frameworks\":{\"private-target\":{\"downloadDependencies\":null}}}}")]
    public async Task MalformedDownloadContainer_RefusesWithSanitizedPublicContract(string json)
    {
        var root = Path.Combine(TestTempRoot.Current, "malformed-download-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var assetsPath = Path.Combine(root, "project.assets.json");
            await File.WriteAllTextAsync(assetsPath, json);
            var policy = new OwnedTestWorkspacePathPolicy();
            var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => InstalledPackageAuthority.CreateAsync(
                assetsPath, System.Text.Encoding.UTF8.GetBytes(json), [],
                (paths, ct) => policy.ValidateAsync("malformed-download", paths, ct), CancellationToken.None));
            StringAssert.Contains(failure.Message, "Installed package provenance is unavailable");
            Assert.IsFalse(failure.Message.Contains("private-", StringComparison.Ordinal));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native authority fixture directory cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("member")]
    [DataRow("archive")]
    [DataRow("assets")]
    [DataRow("missing")]
    [DataRow("grant")]
    public async Task RealDownloadOnlyInstallation_ProvesExactPayloadAndRetainsCurrentEpoch(string mutation)
    {
        var fixture = await CreateDownloadAsync();
        Exception? usingCleanupPrimary4 = null;
        try
        {
            var policy = new OwnedTestWorkspacePathPolicy();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => policy.ValidateAsync("download", paths, ct);
            var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None);
            var installation = proof.Installations(PackageId + "/1.0.0").Single();
            var member = Path.Combine(installation, "tools", "Marker.txt");
            Assert.IsTrue(proof.Files.Contains(member, FileSystemPath.Comparer),
                "PackageDownload payload has no libraries entry; its exact installed members still require independent archive proof.");
            Assert.IsFalse(proof.Files.Contains(Path.Combine(installation, "unlisted.txt"), FileSystemPath.Comparer));
            CollectionAssert.AreEquivalent(proof.Files.Append(fixture.Assets).ToArray(), proof.RequiresCurrentGrant.ToArray());
            await proof.AssertCurrentAsync(Authorize, CancellationToken.None);
            if (mutation == "grant")
            {
                var archive = proof.Files.Single(path => path.EndsWith(".nupkg", StringComparison.Ordinal));
                var locked = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None);
                Exception? usingCleanupPrimary3 = null;
                try
                {
                    Task Deny(IReadOnlyList<string> paths, CancellationToken ct)
                    {
                        if (paths.Any(path => FileSystemPath.IsStrictDescendant(fixture.Cache, path)))
                            throw new UnauthorizedAccessException("The download cache grant was revoked.");
                        return Authorize(paths, ct);
                    }
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => proof.AssertCurrentAsync(Deny, CancellationToken.None));
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                        BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Deny, CancellationToken.None));

                }
                catch (Exception failure) { usingCleanupPrimary3 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary3, locked); }
            }
            else
            {
                var path = mutation switch
                {
                    "assets" => fixture.Assets,
                    "archive" => proof.Files.Single(value => value.EndsWith(".nupkg", StringComparison.Ordinal)),
                    _ => member
                };
                if (mutation == "missing") File.Delete(path);
                else await File.AppendAllTextAsync(path, "changed");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => proof.AssertCurrentAsync(Authorize, CancellationToken.None));
                if (mutation is "member" or "missing" or "archive")
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                        BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None));
            }

        }
        catch (Exception failure) { usingCleanupPrimary4 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary4, fixture); }
    }

    [TestMethod]
    [DataRow("[1.0.0, 2.0.0)")]
    [DataRow("1.0.0")]
    [DataRow("1.*")]
    public async Task DownloadSelection_RequiresAnExactClosedVersion(string selection)
    {
        var fixture = await CreateDownloadAsync();
        Exception? usingCleanupPrimary2 = null;
        try
        {
            var assets = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Assets))!.AsObject();
            var framework = assets["project"]!["frameworks"]!.AsObject().Single().Value!;
            framework["downloadDependencies"]![0]!["version"] = selection;
            await File.WriteAllTextAsync(fixture.Assets, assets.ToJsonString());
            var policy = new OwnedTestWorkspacePathPolicy();
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => BuildInputProvenance.ReadPackagesAsync(
                [fixture.Assets], [], (paths, ct) => policy.ValidateAsync("download", paths, ct), CancellationToken.None));

        }
        catch (Exception failure) { usingCleanupPrimary2 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary2, fixture); }
    }

    private static async Task<NuGetInstallationFixture> CreateDownloadAsync()
    {
        var fixture = await NuGetInstallationFixture.CreateAsync(PackageId, "1.0.0",
            new Dictionary<string, byte[]> { ["tools/Marker.txt"] = "independently verified download"u8.ToArray() });
        try
        {
            var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
            var project = XDocument.Load(projectPath);
            project.Descendants("PackageReference").Single().ReplaceWith(new XElement("PackageDownload",
                new XAttribute("Include", PackageId), new XAttribute("Version", "[1.0.0]")));
            await File.WriteAllTextAsync(projectPath, project.ToString());
            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, projectPath,
                MsBuildCommandProperties.Append(["restore", projectPath, "--force", "--nologo", "--source", Path.Combine(fixture.Root, "feed"),
                    "--packages", fixture.Cache], fixture.LoadProperties), CancellationToken.None);
            Assert.IsTrue(restored.Succeeded, "The native restore must produce the download-only assets contract.");
            var assets = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Assets));
            Exception? usingCleanupPrimary1 = null;
            try
            {
                Assert.AreEqual(0, assets.RootElement.GetProperty("libraries").EnumerateObject().Count());
                var download = assets.RootElement.GetProperty("project").GetProperty("frameworks").EnumerateObject().Single()
                    .Value.GetProperty("downloadDependencies").EnumerateArray().Single();
                Assert.AreEqual(PackageId, download.GetProperty("name").GetString());
                Assert.AreEqual("[1.0.0, 1.0.0]", download.GetProperty("version").GetString());
                return fixture;

            }
            catch (Exception failure) { usingCleanupPrimary1 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary1, assets); }
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.Dispose("Download fixture cleanup failed.", failure, fixture);
            throw;
        }
    }
}
