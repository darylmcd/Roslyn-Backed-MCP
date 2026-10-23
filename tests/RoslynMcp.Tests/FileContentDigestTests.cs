using System.Security.Cryptography;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FileContentDigestTests
{
    [TestMethod]
    public void Digest_IsFixedSizeImmutable_AndMissingDiffersFromEmpty()
    {
        foreach (var length in new[] { 0, 31, 33 })
            Assert.ThrowsExactly<ArgumentException>(() => FileContentDigest.FromHash(new byte[length]));
        var hash = SHA256.HashData("owned"u8);
        var expected = hash.ToArray();
        var digest = FileContentDigest.FromHash(hash);
        hash[0] ^= 0xff;
        CollectionAssert.AreEqual(expected, digest.Hash.ToArray());
        Assert.IsNull(FileContentDigest.FromBytes(null));
        var empty = FileContentDigest.FromBytes([]);
        Assert.IsNotNull(empty);
        CollectionAssert.AreEqual(SHA256.HashData(Array.Empty<byte>()), empty.Hash.ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4097)]
    [DataRow(1048577)]
    public Task PhysicalDigest_HashesExactWholeFile_AndReleasesReadLease(int length)
        => WithRootAsync(async root =>
        {
            var bytes = new byte[length];
            new Random(87).NextBytes(bytes);
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, bytes);
            var digest = await PhysicalInputReader.ReadDigestAsync(path, CancellationToken.None);
            CollectionAssert.AreEqual(SHA256.HashData(bytes), digest.Hash.ToArray());
            FileStream? exclusive = null;
            Exception? primary = null;
            try
            {
                exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.AreEqual((long)length, exclusive.Length);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Digest exclusive-reader cleanup failed.", primary, exclusive); }
        });

    [TestMethod]
    public Task PhysicalDigest_MissingIsNull_DirectoryAndAliasRemainRefusals()
        => WithRootAsync(async root =>
        {
            foreach (var path in new[] { Path.Combine(root, "missing.bin"), Path.Combine(root, "absent", "missing.bin") })
                Assert.IsNull(await FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                FileSnapshotCapture.ReadIfPresentAsync(root, PhysicalInputReader.ReadDigestAsync, CancellationToken.None));
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            var alias = Path.Combine(root, "alias");
            if (!TestFixtureFileSystem.TryCreateDirectoryLink(alias, target))
                Assert.Inconclusive("Filesystem links are unavailable in this environment.");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                FileSnapshotCapture.ReadIfPresentAsync(Path.Combine(alias, "absent.bin"), PhysicalInputReader.ReadDigestAsync, CancellationToken.None));
        });

    [TestMethod]
    public Task PhysicalDigest_ExclusiveSharingFailureIsNotMissing_AndRetainsShareRead()
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            FileStream? blocker = null;
            Exception? blockedPrimary = null;
            try
            {
                blocker = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                await Assert.ThrowsAsync<IOException>(() =>
                    FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, CancellationToken.None));
            }
            catch (Exception failure) { blockedPrimary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Digest sharing blocker cleanup failed.", blockedPrimary, blocker); }
            if (OperatingSystem.IsWindows())
            {
                FileStream? writer = null;
                Exception? writerPrimary = null;
                try
                {
                    writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
                    await Assert.ThrowsAsync<IOException>(() => PhysicalInputReader.ReadDigestAsync(path, CancellationToken.None));
                }
                catch (Exception failure) { writerPrimary = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Digest shared-writer cleanup failed.", writerPrimary, writer); }
            }
            var digest = await PhysicalInputReader.ReadDigestAsync(path, CancellationToken.None);
            CollectionAssert.AreEqual(SHA256.HashData(new byte[] { 1, 2, 3 }), digest.Hash.ToArray());
        });

    [TestMethod]
    public Task PhysicalDigest_PreCancellationPreservesTokenAndDoesNotRetainHandle()
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.bin");
            await File.WriteAllBytesAsync(path, [1]);
            CancellationTokenSource? cancellation = null;
            FileStream? exclusive = null;
            Exception? primary = null;
            try
            {
                cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => PhysicalInputReader.ReadDigestAsync(path, cancellation.Token));
                Assert.AreEqual(cancellation.Token, failure.CancellationToken);
                exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Assert.AreEqual(1L, exclusive.Length);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Digest cancellation fixture cleanup failed.", primary, exclusive, cancellation); }
        });

    [TestMethod]
    [DataRow("stable")]
    [DataRow("changed")]
    [DataRow("missing")]
    public Task Receipt_UsesCurrentVirtualDigestProviderInsteadOfPhysicalBytes(string state)
        => WithRootAsync(async root =>
        {
            var path = Path.Combine(root, "input.cs");
            var original = "class Original { }"u8.ToArray();
            await File.WriteAllBytesAsync(path, original);
            var arguments = CSharpCommandLineParser.Default.Parse([], root, sdkDirectory: null);
            var receipt = new CompilerArgumentParseReceipt(arguments,
                [new CompilerArgumentInputObservation(new PhysicalInputIdentity(path, true, [.. SHA256.HashData(original)]),
                    CompilerArgumentInputRole.SourceMatch)], [], []);
            var reads = 0;
            var grants = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken token)
            {
                grants++;
                return Task.CompletedTask;
            }
            Task<byte[]?> VirtualRead(string requested, CancellationToken token)
            {
                Assert.AreEqual(path, requested);
                reads++;
                return Task.FromResult<byte[]?>(state switch { "missing" => null, "changed" => "class VirtualChange { }"u8.ToArray(), _ => original.ToArray() });
            }
            Task ValidateAsync() => receipt.AssertCurrentAsync(Authorize,
                (requested, token) => FileContentDigest.FromReaderAsync(requested, VirtualRead, token), CancellationToken.None);
            if (state != "stable") await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(ValidateAsync);
            else await ValidateAsync();
            Assert.AreEqual(1, reads);
            Assert.IsTrue(grants > 0);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        });

    [TestMethod]
    public async Task SnapshotOnlyDigest_DoesNotReadPhysicalPath_AndCannotAuthorizeIt()
    {
        var invalid = "invalid\0physical";
        Assert.IsNull(await SolutionDiffReadContext.SnapshotOnly.ReadDigestAsync(invalid, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            SolutionDiffReadContext.SnapshotOnly.AuthorizePathsAsync([invalid], CancellationToken.None));
    }

    private static async Task WithRootAsync(Func<string, Task> body)
    {
        var lexical = Path.Combine(TestTempRoot.Current, nameof(FileContentDigestTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(lexical);
            var root = PhysicalPathResolver.Resolve(lexical);
            await body(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Digest fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(lexical)));
        }
    }
}
