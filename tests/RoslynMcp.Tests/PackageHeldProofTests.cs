using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PackageHeldProofTests
{
    [TestMethod]
    [DataRow("stable")]
    [DataRow("revoke")]
    [DataRow("tail")]
    [DataRow("replace")]
    [DataRow("leaf-link")]
    [DataRow("parent-link")]
    [DataRow("junction")]
    public async Task CurrentPackageProof_RetainsGrantHeldPathAndFullBytes(string change)
    {
        if (change == "junction" && !OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("NTFS mount-point junctions are a Windows filesystem concept.");
            return;
        }
        var bytes = new byte[1024 * 1024 + 7];
        new Random(170117).NextBytes(bytes);
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.HeldProof", "1.0.0",
            new Dictionary<string, byte[]> { ["contentFiles/any/any/input.bin"] = bytes });
        Exception? fixturePrimary = null;
        try
        {
        var policy = new OwnedTestWorkspacePathPolicy();
        var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [],
            (paths, ct) => policy.ValidateAsync("held-proof", paths, ct), CancellationToken.None);
        var target = proof.Files.Single(path => path.EndsWith("input.bin", StringComparison.Ordinal));
        var parent = Path.GetDirectoryName(target)!;
        var moved = change is "parent-link" or "junction" ? parent + ".saved" : target + ".saved";
        var changed = false;
        var linked = false;
        var grants = new List<string[]>();
        FileStream? blocker = change == "revoke" ? new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        Exception? primary = null;
        try
        {
            if (change is "leaf-link" or "parent-link")
            {
                // Capability refusal must occur outside Assert.ThrowsExactly: otherwise the
                // assertion wraps Inconclusive as a failed security expectation.
                var probe = Path.Combine(fixture.Root, "link-capability");
                var available = false;
                Exception? probeFailure = null;
                try
                {
                    available = change == "leaf-link"
                        ? TestFixtureFileSystem.TryCreateFileLink(probe, target)
                        : TestFixtureFileSystem.TryCreateDirectoryLink(probe, parent);
                }
                catch (Exception failure) { probeFailure = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Link capability cleanup failed.", probeFailure,
                        CleanupFailureCollector.FromAction(() =>
                        {
                            if (!available) return;
                            if (change == "leaf-link") File.Delete(probe);
                            else Directory.Delete(probe);
                        }));
                }
                if (!available) Assert.Inconclusive("The fixture filesystem cannot create the required link.");
            }
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                grants.Add(paths.ToArray());
                if (!paths.Contains(target, FileSystemPath.Comparer))
                {
                    await policy.ValidateAsync("held-proof", paths, ct);
                    return;
                }
                if (change == "revoke") throw new UnauthorizedAccessException("Fixture grant revoked.");
                await policy.ValidateAsync("held-proof", paths, ct);
                if (changed || change == "stable") return;
                // Mutate after the current grant, before physical acquisition. No grants are cached.
                changed = true;
                if (change == "tail")
                {
                    using var writer = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    writer.Position = bytes.Length - 1;
                    writer.WriteByte((byte)(bytes[^1] ^ 0xff));
                }
                else if (change is "parent-link" or "junction")
                {
                    Directory.Move(parent, moved);
                    if (change == "junction" && OperatingSystem.IsWindows())
                    {
                        WindowsDirectoryJunction.Create(parent, moved);
                        linked = true;
                    }
                    else linked = TestFixtureFileSystem.TryCreateDirectoryLink(parent, moved);
                    Assert.IsTrue(linked, "The post-grant directory link must be created after its capability probe succeeded.");
                }
                else
                {
                    File.Move(target, moved);
                    if (change == "leaf-link")
                    {
                        linked = TestFixtureFileSystem.TryCreateFileLink(target, moved);
                        Assert.IsTrue(linked, "The post-grant file link must be created after its capability probe succeeded.");
                    }
                    else
                    {
                        var replacement = bytes.ToArray();
                        replacement[^1] ^= 0xff;
                        await File.WriteAllBytesAsync(target, replacement, ct);
                    }
                }
            }
            if (change == "stable")
            {
                await proof.AssertPackageFilesCurrentAsync(Authorize, CancellationToken.None);
                Assert.HasCount(1, grants, "The complete retained package inventory needs one current grant phase before physical capture.");
                CollectionAssert.AreEquivalent(proof.Files.Where(path => proof.RequiresCurrentGrant.Contains(path, FileSystemPath.Comparer)).ToArray(), grants.Single());
            }
            else if (change == "revoke")
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => proof.AssertPackageFilesCurrentAsync(Authorize, CancellationToken.None));
            else if (change is "tail" or "replace")
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => proof.AssertPackageFilesCurrentAsync(Authorize, CancellationToken.None));
            else
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => proof.AssertPackageFilesCurrentAsync(Authorize, CancellationToken.None));
            if (change != "stable" && change != "revoke") Assert.IsTrue(changed);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Package path fixture restoration failed.", primary,
                CleanupFailureCollector.FromAction(() => blocker?.Dispose()),
                CleanupFailureCollector.FromAction(() =>
                {
                    if (!changed || change == "tail") return;
                    if (change is "parent-link" or "junction")
                    {
                        if (linked) Directory.Delete(parent);
                        Directory.Move(moved, parent);
                    }
                    else
                    {
                        if (File.Exists(target) || linked) File.Delete(target);
                        File.Move(moved, target);
                    }
                }));
        }
        }
        catch (Exception failure) { fixturePrimary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Package proof fixture cleanup failed.", fixturePrimary, fixture); }
    }
}
