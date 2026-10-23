using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InstalledPackageAuthorityTests
{
    [TestMethod]
    public async Task PolicyUnauthorizedDetails_AreSanitizedWithoutLosingTypedRefusal()
    {
        NuGetInstallationFixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.PrivatePolicyDetails", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned fixture"u8.ToArray() });
            const string detail = "private-fixture-policy-detail";
            var original = WorkspacePathPolicyRefusal.Mark(new UnauthorizedAccessException(detail, new IOException(detail)),
                WorkspacePathPolicyRefusalKind.AliasedPath);
            original.Data[detail] = detail;
            var policy = new OwnedTestWorkspacePathPolicy();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (paths.Any(path => FileSystemPath.IsStrictDescendant(fixture.Cache, path))) throw original;
                return policy.ValidateAsync("private-policy", paths, ct);
            }
            var failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None));
            Assert.AreNotSame(original, failure);
            Assert.IsNull(failure.InnerException);
            Assert.IsFalse(failure.ToString().Contains(detail, StringComparison.Ordinal));
            Assert.IsFalse(failure.Data.Contains(detail));
            Assert.AreEqual(true, failure.Data[InstalledPackageAuthority.DependencyGrantRequiredKey]);
            Assert.IsTrue(WorkspacePathPolicyRefusal.Is(failure, WorkspacePathPolicyRefusalKind.AliasedPath));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task DependencyGrantUnavailable_RetainsSemanticWorkspaceAndRequiresGrantBeforeCertifiedReload()
    {
        NuGetInstallationFixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/Example.dll"] = await File.ReadAllBytesAsync(typeof(object).Assembly.Location) });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            await WithManagerAsync(new CacheDenyPolicy(fixture.Cache), async manager =>
            {
                var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
                Assert.IsFalse(status.RestoreRequired,
                    "The package is restored; repeating restore cannot supply its missing current read grant.");
                Assert.IsTrue(status.WorkspaceDiagnostics.Any(diagnostic => diagnostic.Id == "WORKSPACE_COMPILER_CONTEXT_UNAVAILABLE"
                    && diagnostic.Message.Contains("Authorize", StringComparison.Ordinal)));
                var solution = manager.GetCurrentSolution(status.WorkspaceId);
                Assert.HasCount(1, solution.ProjectIds);
                var failure = Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerContextCertificate.Require(solution));
                StringAssert.Contains(failure.Message, "Restoring alone does not authorize");
            });
            await WithManagerAsync(new OwnedTestWorkspacePathPolicy(), async manager =>
            {
                var status = await manager.LoadAsync(project, fixture.LoadProperties, CancellationToken.None);
                _ = NativeCompilerContextCertificate.Require(manager.GetCurrentSolution(status.WorkspaceId));
            });
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    [DataRow("deny")]
    [DataRow("package")]
    [DataRow("assets")]
    public async Task ExpectedGraph_PreservesCurrentCacheGrantAndExactPackageAndAssetsEpoch(string change)
    {
        NuGetInstallationFixture? fixture = null;
        AdhocWorkspace? workspace = null;
        FileStream? locked = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0",
                new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
            var policy = new OwnedTestWorkspacePathPolicy();
            var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
                (paths, ct) => policy.ValidateAsync("expected", paths, ct), CancellationToken.None);
            workspace = new AdhocWorkspace();
            var properties = ImmutableHashSet<string>.Empty;
            var expected = new PhysicalGraphExpectation(workspace.CurrentSolution, static path => path, properties,
                EvaluatedExecutionIdentity.Capture(workspace.CurrentSolution, static path => path, properties, CancellationToken.None), proof.Inputs, proof, []);
            await expected.AssertBuildInputsAsync("expected", policy, CancellationToken.None);
            if (change == "deny")
            {
                var archive = proof.Files.Single(path => path.EndsWith(".nupkg", StringComparison.Ordinal));
                locked = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None);
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                    expected.AssertBuildInputsAsync("expected", new CacheDenyPolicy(fixture.Cache), CancellationToken.None));
            }
            else
            {
                var target = change == "assets" ? fixture.Assets : proof.Files.Single(path => path.EndsWith("Input.cs", StringComparison.Ordinal));
                await File.AppendAllTextAsync(target, " ");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    expected.AssertBuildInputsAsync("expected", policy, CancellationToken.None));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => locked?.Dispose()),
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task RealCustomInstallation_CurrentGrantPrecedesManifestReadAndRemainsRequired()
    {
        var payload = await File.ReadAllBytesAsync(typeof(object).Assembly.Location);
        NuGetInstallationFixture? fixture = null;
        FileStream? locked = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0",
                new Dictionary<string, byte[]> { ["lib/net10.0/Example.dll"] = payload });
            var policy = new OwnedTestWorkspacePathPolicy();
            var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
                (paths, ct) => policy.ValidateAsync("installed", paths, ct), CancellationToken.None);
            Assert.IsTrue(proof.Files.Length > 0);
            var payloadPath = Path.Combine(fixture.Cache, "owned.example", "1.0.0", "lib", "net10.0", "Example.dll");
            var captured = proof.Inputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, payloadPath));
            Assert.IsTrue(captured.Exists);
            CollectionAssert.AreEqual(System.Security.Cryptography.SHA256.HashData(payload), captured.ContentHash.ToArray(),
                "The issued installation identity must bind the exact independently supplied archive payload.");
            CollectionAssert.AreEquivalent(proof.Files.Append(fixture.Assets).ToArray(), proof.RequiresCurrentGrant.ToArray());
            var archive = proof.Files.Single(path => path.EndsWith(".nupkg", StringComparison.Ordinal));
            locked = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None);
            var denied = 0;
            Task DenyCache(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (paths.Any(path => FileSystemPath.IsStrictDescendant(fixture.Cache, path)))
                {
                    denied++;
                    throw new UnauthorizedAccessException("The current request excludes the fixture cache.");
                }
                return policy.ValidateAsync("installed", paths, ct);
            }
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], DenyCache, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => proof.AssertCurrentAsync(DenyCache, CancellationToken.None));
            Assert.AreEqual(2, denied, "Both issuance and revalidation must reach current authorization before the locked archive read.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => locked?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    public async Task RealInstalledExtraction_MutationCannotBeRecertifiedFromAssetsInventory()
    {
        NuGetInstallationFixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.Example", "1.0.0",
                new Dictionary<string, byte[]> { ["contentFiles/cs/any/Input.cs"] = "class Input { }"u8.ToArray() });
            var policy = new OwnedTestWorkspacePathPolicy();
            var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
                (paths, ct) => policy.ValidateAsync("installed", paths, ct), CancellationToken.None);
            var file = proof.Files.Single(path => path.EndsWith("Input.cs", StringComparison.Ordinal));
            await File.AppendAllTextAsync(file, " // changed");
            await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() =>
                BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
                    (paths, ct) => policy.ValidateAsync("installed", paths, ct), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualSignedDependency_UsesBootstrapCacheAndRejectsForgedArchiveBytes(bool forgeArchive)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, "signed-installation-" + Guid.NewGuid().ToString("N"));
        JsonDocument? actual = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var repository = TestFixtureFileSystem.FindRepositoryRoot();
            var assetsPath = Path.Combine(repository, "src", "RoslynMcp.Roslyn", "obj", "project.assets.json");
            var policy = new OwnedTestWorkspacePathPolicy();
            await policy.ValidateAsync("signed", [assetsPath], CancellationToken.None);
            actual = JsonDocument.Parse(await File.ReadAllBytesAsync(assetsPath));
            var library = actual.RootElement.GetProperty("libraries").EnumerateObject()
                .Single(value => value.Name.Equals("NuGet.Versioning/6.3.4", StringComparison.OrdinalIgnoreCase));
            var candidate = new JsonObject
            {
                ["packageFolders"] = JsonNode.Parse(actual.RootElement.GetProperty("packageFolders").GetRawText()),
                ["libraries"] = new JsonObject { [library.Name] = JsonNode.Parse(library.Value.GetRawText()) }
            };
            var approved = Path.Combine(root, "project.assets.json");
            await File.WriteAllTextAsync(approved, candidate.ToJsonString());
            var proof = await BuildInputProvenance.ReadPackagesAsync([approved], [], (paths, ct) =>
            {
                Assert.IsTrue(paths.All(path => FileSystemPath.IsStrictDescendant(root, path)),
                    "A bootstrap-certified installed dependency needs no mutable cache-root grant.");
                return policy.ValidateAsync("signed", paths, ct);
            }, CancellationToken.None);
            Assert.AreEqual(1, proof.SignedPackageCount, "The control must exercise a real signed NuGet package.");
            Assert.AreEqual(proof.SignedPackageCount, proof.RetainPackageFilesFrom(proof).SignedPackageCount,
                "Retaining the same installation must not count its signed archive twice.");
            CollectionAssert.AreEquivalent(new[] { approved }, proof.RequiresCurrentGrant.ToArray());
            await proof.AssertCurrentAsync((paths, _) =>
            {
                Assert.IsTrue(paths.All(path => FileSystemPath.Comparer.Equals(path, approved)));
                return Task.CompletedTask;
            }, CancellationToken.None);
            if (forgeArchive)
            {
                var installation = proof.Installations(library.Name).Single();
                var privateCache = Path.Combine(root, "private-cache");
                var privateInstallation = Path.Combine(privateCache, "nuget.versioning", "6.3.4");
                foreach (var file in proof.Files)
                {
                    var target = Path.Combine(privateInstallation, Path.GetRelativePath(installation, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file));
                }
                var originalAssets = await File.ReadAllBytesAsync(approved);
                candidate["packageFolders"] = new JsonObject { [privateCache] = new JsonObject() };
                await File.WriteAllTextAsync(approved, candidate.ToJsonString());
                _ = await BuildInputProvenance.ReadPackagesAsync([approved], [],
                    (paths, ct) => policy.ValidateAsync("private-signed", paths, ct), CancellationToken.None);
                var archivePath = Path.Combine(privateInstallation, "nuget.versioning.6.3.4.nupkg");
                var (changedEntry, replacement) = await RewriteArchiveEntryAsync(archivePath);
                await File.WriteAllBytesAsync(Path.Combine(privateInstallation, changedEntry), replacement);
                await File.WriteAllTextAsync(archivePath + ".sha512", Convert.ToBase64String(SHA512.HashData(await File.ReadAllBytesAsync(archivePath))));
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => BuildInputProvenance.ReadPackagesAsync([approved], [],
                    (paths, ct) => policy.ValidateAsync("private-signed", paths, ct), CancellationToken.None));
                // The independent installation was never mutated by the owned adversarial copy.
                await File.WriteAllBytesAsync(approved, originalAssets);
                await proof.AssertCurrentAsync((paths, ct) => policy.ValidateAsync("signed-original", paths, ct), CancellationToken.None);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Signed installation fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => actual?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private static async Task WithManagerAsync(IWorkspacePathPolicy policy, Func<WorkspaceManager, Task> body)
    {
        FileWatcherService? watcher = null;
        WorkspaceManager? manager = null;
        Exception? primary = null;
        try
        {
            watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            manager = new WorkspaceManager(new ForkInvocationPathPolicy(policy), NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), watcher, new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4 });
            await body(manager);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Installed package manager cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => manager?.Dispose()),
                CleanupFailureCollector.FromAction(() => { if (manager is null) watcher?.Dispose(); }));
        }
    }

    private static async Task<(string Entry, byte[] Replacement)> RewriteArchiveEntryAsync(string archivePath)
    {
        ZipArchive? archive = null;
        Stream? output = null;
        Exception? primary = null;
        try
        {
            archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
            var entry = archive.Entries.First(item => item.FullName.EndsWith(".dll", StringComparison.Ordinal));
            var changedEntry = entry.FullName;
            var replacement = await ReadReplacementAsync(entry);
            entry.Delete();
            output = archive.CreateEntry(changedEntry).Open();
            await output.WriteAsync(replacement);
            return (changedEntry, replacement);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Adversarial package archive cleanup failed.", primary,
                () => output is null ? ValueTask.CompletedTask : output.DisposeAsync(),
                CleanupFailureCollector.FromAction(() => archive?.Dispose()));
        }
    }

    private static async Task<byte[]> ReadReplacementAsync(ZipArchiveEntry entry)
    {
        Stream? original = null;
        MemoryStream? bytes = null;
        Exception? primary = null;
        try
        {
            original = entry.Open();
            bytes = new MemoryStream();
            await original.CopyToAsync(bytes);
            return [.. bytes.ToArray(), 0];
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Archive entry reader cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => bytes?.Dispose()),
                () => original is null ? ValueTask.CompletedTask : original.DisposeAsync());
        }
    }

    private sealed class CacheDenyPolicy(string cache) : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            if (paths.Any(path => FileSystemPath.IsStrictDescendant(cache, path)))
                throw new UnauthorizedAccessException("The current request excludes the fixture cache.");
            return new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
        }
    }
}
