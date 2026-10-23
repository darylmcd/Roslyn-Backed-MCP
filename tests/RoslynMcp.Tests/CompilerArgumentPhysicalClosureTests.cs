using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentPhysicalClosureTests
{
    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("file-drift")]
    [DataRow("excluded-file")]
    [DataRow("new-recursive-directory")]
    [DataRow("excluded-directory-kind-drift")]
    public async Task PhysicalClosure_UsesFrozenCompletePathsWithoutAuthorizationCallbacks(string change)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentPhysicalClosureTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            var source = Path.Combine(nested, "Entry.cs");
            var response = Path.Combine(root, "inputs.rsp");
            await File.WriteAllTextAsync(source, "class Entry { }");
            await File.WriteAllTextAsync(response, "-recurse:*.cs");
            var authorizationCalls = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                authorizationCalls++;
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var parser = new CompilerArgumentInputContext(root, Authorize, Read,
                (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), CompilerArgumentInputContext.CreateStagePath,
                CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var receipt = await parser.ParseAsync(["@inputs.rsp"], CancellationToken.None);
            foreach (var selected in new[] { root, nested, source, response })
                Assert.IsTrue(receipt.CurrentGrantPaths.Contains(selected, FileSystemPath.Comparer),
                    "The final grant batch must include all selected and traversed input coordinates.");
            var excluded = new HashSet<string>(FileSystemPath.Comparer);
            if (change is "file-drift" or "excluded-file")
                await File.WriteAllTextAsync(source, "class Changed { }");
            if (change == "excluded-file") excluded.Add(source);
            if (change == "new-recursive-directory")
            {
                Directory.CreateDirectory(Path.Combine(root, "new-child"));
                await File.WriteAllTextAsync(Path.Combine(root, "new-child", "New.cs"), "class New { }");
            }
            if (change == "excluded-directory-kind-drift")
            {
                File.Delete(source);
                Directory.Delete(nested);
                await File.WriteAllTextAsync(nested, "a directory changed to an ordinary file");
                excluded.Add(nested);
            }
            authorizationCalls = 0;
            if (change is "unchanged" or "excluded-file")
                await receipt.AssertPhysicalCurrentAsync(excluded, CancellationToken.None);
            else
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    receipt.AssertPhysicalCurrentAsync(excluded, CancellationToken.None));
            Assert.AreEqual(0, authorizationCalls, "The closing proof must not invoke user authorization after the complete final grant batch.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Compiler physical closure fixture cleanup failed.", primary,
            OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))); }
    }
}
