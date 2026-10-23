using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class InstalledPackageIssuanceGrantTests
{
    [TestMethod]
    [DataRow("hash")]
    [DataRow("metadata")]
    public async Task Issuance_RechecksBatchGrantBeforeInstallationManifestRead(string kind)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.BatchRevocation", "1.0.0",
            new Dictionary<string, byte[]> { ["contentFiles/any/any/Marker.txt"] = "owned payload"u8.ToArray() });
        Exception? usingCleanupPrimary2 = null;
        try
        {
            var directory = Path.Combine(fixture.Cache, "owned.batchrevocation", "1.0.0");
            var archive = Path.Combine(directory, "owned.batchrevocation.1.0.0.nupkg");
            var hash = archive + ".sha512";
            var metadata = Path.Combine(directory, ".nupkg.metadata");
            var target = kind == "hash" ? hash : metadata;
            var policy = new OwnedTestWorkspacePathPolicy();
            var preflightCompleted = false;
            var physicalPreflightRemainsValid = false;
            var denied = 0;
            FileStream? blocker = null;
            Exception? primary = null;
            try
            {
                async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
                {
                    if (preflightCompleted && paths.Contains(target, FileSystemPath.Comparer))
                    {
                        denied++;
                        throw new UnauthorizedAccessException("The installation manifest grant changed after batch approval.");
                    }
                    await policy.ValidateAsync("batch-issuance", paths, ct);
                    if (paths.Contains(archive, FileSystemPath.Comparer)
                        && paths.Contains(hash, FileSystemPath.Comparer)
                        && paths.Contains(metadata, FileSystemPath.Comparer))
                    {
                        Assert.IsFalse(preflightCompleted, "The initial batch must remain an explicit preflight.");
                        // Complete that approval, then revoke only this manifest's grant.
                        // Its physical identity still resolves: only content reads are locked.
                        blocker = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
                        WorkspaceManager.EnsurePhysicalPath(target);
                        physicalPreflightRemainsValid = true;
                        preflightCompleted = true;
                    }
                }

                var failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                    BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None));
                Assert.IsTrue(preflightCompleted);
                Assert.IsTrue(physicalPreflightRemainsValid, "The original metadata-only path preflight must still be valid under the blocker.");
                Assert.AreEqual(true, failure.Data[InstalledPackageAuthority.DependencyGrantRequiredKey]);
                Assert.AreEqual(1, denied, "The current exact manifest grant must be checked before its locked content read.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Installation manifest blocker cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
            }

        }
        catch (Exception failure) { usingCleanupPrimary2 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary2, fixture); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Issuance_RechecksEarlierArchiveGrantBeforeFinalIdentityRead(bool revoke)
    {
        var fixture = await NuGetInstallationFixture.CreateAsync("Owned.Revocation", "1.0.0",
            new Dictionary<string, byte[]> { ["contentFiles/any/any/Marker.txt"] = "owned payload"u8.ToArray() });
        Exception? usingCleanupPrimary1 = null;
        try
        {
            var archive = Path.Combine(fixture.Cache, "owned.revocation", "1.0.0", "owned.revocation.1.0.0.nupkg");
            var payload = Path.Combine(fixture.Cache, "owned.revocation", "1.0.0", "contentFiles", "any", "any", "Marker.txt");
            var policy = new OwnedTestWorkspacePathPolicy();
            var payloadAuthorized = false;
            var denied = 0;
            FileStream? blocker = null;
            Exception? primary = null;
            try
            {
                async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
                {
                    if (revoke && payloadAuthorized && paths.Contains(archive, FileSystemPath.Comparer))
                    {
                        denied++;
                        throw new UnauthorizedAccessException("The archive grant was revoked while the later payload was authorized.");
                    }
                    await policy.ValidateAsync("issuance", paths, ct);
                    if (paths.Contains(payload, FileSystemPath.Comparer) && !payloadAuthorized)
                    {
                        payloadAuthorized = true;
                        if (revoke)
                            blocker = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                }

                if (revoke)
                {
                    var failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                        BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None));
                    Assert.AreEqual(true, failure.Data[InstalledPackageAuthority.DependencyGrantRequiredKey]);
                    Assert.AreEqual(1, denied, "Current denial must precede the now-locked archive's final identity read.");
                }
                else
                {
                    var proof = await BuildInputProvenance.ReadPackagesAsync([fixture.Assets], [], Authorize, CancellationToken.None);
                    Assert.IsTrue(proof.Files.Contains(archive, FileSystemPath.Comparer));
                    Assert.IsTrue(proof.Files.Contains(payload, FileSystemPath.Comparer));
                    await proof.AssertCurrentAsync(Authorize, CancellationToken.None);
                }
                Assert.IsTrue(payloadAuthorized, "The later payload must be reached after the archive was interpreted.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Package issuance blocker cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
            }

        }
        catch (Exception failure) { usingCleanupPrimary1 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native authority resource cleanup failed.", usingCleanupPrimary1, fixture); }
    }
}
