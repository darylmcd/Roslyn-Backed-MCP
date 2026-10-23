using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentCurrentAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecursiveRevalidation_UsesCurrentPolicyBeforeNewChildIo(bool addDeniedChild)
    {
        await InFixtureAsync(async root =>
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public class Entry { }");
            var child = Path.Combine(root, "new-child");
            var prepared = false;
            var originalCallbacksAfterPreparation = 0;
            Task OriginalAuthorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (prepared) originalCallbacksAfterPreparation++;
                return Task.CompletedTask;
            }
            Task<byte[]?> OriginalRead(string path, CancellationToken ct)
            {
                if (prepared) originalCallbacksAfterPreparation++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            var receipt = await new CompilerArgumentInputContext(root, OriginalAuthorize, OriginalRead, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, OriginalRead, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync(["-recurse:*.cs"], CancellationToken.None);
            prepared = true;
            if (addDeniedChild)
            {
                Directory.CreateDirectory(child);
                await File.WriteAllTextAsync(Path.Combine(child, "Foreign.cs"), "public class Foreign { }");
            }
            var currentProbes = 0;
            var deniedProbes = 0;
            var childReads = 0;
            Task CurrentAuthorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                currentProbes++;
                if (paths.Any(path => FileSystemPath.Comparer.Equals(path, child) || FileSystemPath.IsStrictDescendant(child, path)))
                {
                    deniedProbes++;
                    throw new UnauthorizedAccessException("Current caller denies the newly present child.");
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> CurrentRead(string path, CancellationToken ct)
            {
                if (FileSystemPath.IsStrictDescendant(child, path)) childReads++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            if (addDeniedChild)
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => receipt.AssertCurrentAsync(CurrentAuthorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, CurrentRead, digestToken), CancellationToken.None));
            else await receipt.AssertCurrentAsync(CurrentAuthorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, CurrentRead, digestToken), CancellationToken.None);
            Assert.IsTrue(currentProbes > 0);
            Assert.AreEqual(addDeniedChild ? 1 : 0, deniedProbes);
            Assert.AreEqual(0, childReads);
            Assert.AreEqual(0, originalCallbacksAfterPreparation, "Historical policy/read closures must not survive receipt issuance.");
        });
    }

    [TestMethod]
    public async Task ChangedInputKind_RefusesBeforeCallingCurrentByteReader()
    {
        await InFixtureAsync(async root =>
        {
            var response = Path.Combine(root, "input.rsp");
            await File.WriteAllTextAsync(response, "/define:INITIAL");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync(["@input.rsp"], CancellationToken.None);
            File.Delete(response);
            Directory.CreateDirectory(response);
            var reads = 0;
            Task<byte[]?> CurrentRead(string path, CancellationToken ct)
            {
                reads++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, CurrentRead, digestToken), CancellationToken.None));
            Assert.AreEqual(0, reads);
        });
    }

    private static async Task InFixtureAsync(Func<string, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentCurrentAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await body(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Current compiler authority fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
