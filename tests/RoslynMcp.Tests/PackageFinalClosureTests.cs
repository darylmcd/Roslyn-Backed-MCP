using Microsoft.Win32.SafeHandles;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PackageFinalClosureTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectingAssetGrant_CannotInvalidateAnEarlierPackagePayload(bool issuance)
    {
        NuGetInstallationFixture? fixture = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.FinalPackageProof", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "original package payload"u8.ToArray() });
            var marker = Path.Combine(fixture.Cache, "owned.finalpackageproof", "1.0.0", "marker.txt");
            var owned = new OwnedTestWorkspacePathPolicy();
            var armed = issuance;
            var payloadObserved = false;
            var mutations = 0;
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                await owned.ValidateAsync("package-final-closure", paths, ct);
                if (paths.Contains(marker, FileSystemPath.Comparer)) payloadObserved = true;
                if (!armed || !payloadObserved || mutations != 0
                    || !paths.Contains(fixture.Assets, FileSystemPath.Comparer)) return;
                mutations++;
                await File.WriteAllTextAsync(marker, "changed by selecting asset grant", ct);
            }
            var bytes = await File.ReadAllBytesAsync(fixture.Assets);
            InstalledPackageAuthority? proof = null;
            if (!issuance)
            {
                proof = await InstalledPackageAuthority.CreateAsync(fixture.Assets, bytes, [fixture.Root], Authorize, CancellationToken.None);
                CollectionAssert.Contains(proof.Files.ToArray(), marker);
                payloadObserved = false;
                armed = true;
            }
            Exception? refused = null;
            try
            {
                if (issuance)
                    _ = await InstalledPackageAuthority.CreateAsync(fixture.Assets, bytes, [fixture.Root], Authorize, CancellationToken.None);
                else
                    await (proof ?? throw new AssertFailedException("The actual retained package proof must exist.")).AssertCurrentAsync(Authorize, CancellationToken.None);
            }
            catch (Exception failure) { refused = failure; }
            Assert.IsTrue(payloadObserved);
            Assert.AreEqual(1, mutations);
            Assert.AreEqual("changed by selecting asset grant", await File.ReadAllTextAsync(marker));
            Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType(),
                "A completed package proof cannot retain payload bytes invalidated by its final selecting-assets callback.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Package final closure fixture cleanup failed.", primary, fixture); }
    }

    [TestMethod]
    public async Task MergedPackageDiscovery_ClosesEarlierProofAfterLaterAssetCallbacks()
    {
        NuGetInstallationFixture? first = null;
        NuGetInstallationFixture? second = null;
        Exception? primary = null;
        try
        {
            first = await NuGetInstallationFixture.CreateAsync("Owned.FirstPackageProof", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "earlier payload"u8.ToArray() });
            second = await NuGetInstallationFixture.CreateAsync("Owned.SecondPackageProof", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "later payload"u8.ToArray() });
            var marker = Path.Combine(first.Cache, "owned.firstpackageproof", "1.0.0", "marker.txt");
            var owned = new OwnedTestWorkspacePathPolicy();
            var firstPayloadObserved = false;
            var mutations = 0;
            async Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                await owned.ValidateAsync("merged-package-final", paths, ct);
                if (paths.Contains(marker, FileSystemPath.Comparer)) firstPayloadObserved = true;
                if (!firstPayloadObserved || mutations != 0 || !paths.Contains(second.Assets, FileSystemPath.Comparer)) return;
                mutations++;
                await File.WriteAllTextAsync(marker, "changed by later asset discovery", ct);
            }
            Exception? refused = null;
            try
            {
                _ = await BuildInputProvenance.ReadPackagesAsync([first.Assets, second.Assets],
                    [first.Root, second.Root], Authorize, CancellationToken.None);
            }
            catch (Exception failure) { refused = failure; }
            Assert.IsTrue(firstPayloadObserved);
            Assert.AreEqual(1, mutations);
            Assert.AreEqual("changed by later asset discovery", await File.ReadAllTextAsync(marker));
            Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Merged package closure fixture cleanup failed.", primary, second, first); }
    }

    [TestMethod]
    public async Task MandatoryAssetsDenial_PrecedesReadsWithoutBecomingDependencyRefusal()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows native sharing premise."); return; }
        NuGetInstallationFixture? fixture = null;
        SafeFileHandle? blocked = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.AssetGrantOrder", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned payload"u8.ToArray() });
            var owned = new OwnedTestWorkspacePathPolicy();
            Task Approve(IReadOnlyList<string> paths, CancellationToken ct) => owned.ValidateAsync("asset-order", paths, ct);
            var proof = await InstalledPackageAuthority.CreateAsync(fixture.Assets, await File.ReadAllBytesAsync(fixture.Assets),
                [fixture.Root], Approve, CancellationToken.None);
            blocked = File.OpenHandle(proof.Files[0], FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.ThrowsExactly<IOException>(() => File.ReadAllBytes(proof.Files[0]));
            var mandatoryRefusal = new UnauthorizedAccessException("The selecting asset grant is revoked.");
            Task DenyAssets(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (paths.Contains(fixture.Assets, FileSystemPath.Comparer)) throw mandatoryRefusal;
                return Approve(paths, ct);
            }
            var refusal = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
                () => proof.AssertCurrentAsync(DenyAssets, CancellationToken.None));
            Assert.AreSame(mandatoryRefusal, refusal, "Mandatory assets denial must keep its original refusal identity.");
            Assert.IsFalse(refusal.Data.Contains(InstalledPackageAuthority.DependencyGrantRequiredKey),
                "A mandatory input refusal must propagate instead of degrading semantic loading as a package-cache denial.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Mandatory asset grant fixture cleanup failed.", primary, blocked, fixture); }
    }

    [TestMethod]
    public async Task CompleteCurrentPackageGrant_PrecedesAnyNativePayloadRead()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows native sharing premise."); return; }
        NuGetInstallationFixture? fixture = null;
        SafeFileHandle? blocked = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.PackageReadOrder", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned payload"u8.ToArray() });
            var owned = new OwnedTestWorkspacePathPolicy();
            Task Approve(IReadOnlyList<string> paths, CancellationToken ct) => owned.ValidateAsync("package-order", paths, ct);
            var proof = await InstalledPackageAuthority.CreateAsync(fixture.Assets, await File.ReadAllBytesAsync(fixture.Assets),
                [fixture.Root], Approve, CancellationToken.None);
            var paths = proof.Files.Where(path => proof.RequiresCurrentGrant.Contains(path, FileSystemPath.Comparer)).ToArray();
            Assert.IsGreaterThanOrEqualTo(2, paths.Length, "The control requires distinct first and later payload inputs.");
            blocked = File.OpenHandle(paths[0], FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.ThrowsExactly<IOException>(() => File.ReadAllBytes(paths[0]), "The first native payload read must independently fail.");
            var attempts = 0;
            Task DenyLater(IReadOnlyList<string> requested, CancellationToken ct)
            {
                if (requested.Contains(paths[^1], FileSystemPath.Comparer))
                {
                    attempts++;
                    throw new UnauthorizedAccessException("The later package input grant is revoked.");
                }
                return Approve(requested, ct);
            }
            var refusal = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
                () => proof.AssertPackageFilesCurrentAsync(DenyLater, CancellationToken.None));
            Assert.AreEqual(1, attempts);
            Assert.AreEqual(true, refusal.Data[InstalledPackageAuthority.DependencyGrantRequiredKey],
                "The actual grant refusal must preserve its typed provenance before any native payload read.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.Dispose("Package grant ordering fixture cleanup failed.", primary, blocked, fixture); }
    }
}
