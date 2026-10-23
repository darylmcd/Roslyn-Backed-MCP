using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class AnalyzerInputCatalogTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Capture_DeniesDirectoryOrDiscoveredSiblingBeforeAnyByteRead(bool denySibling)
    {
        var root = Path.Combine(TestTempRoot.Current, "catalog-denial-" + Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var main = Path.Combine(root, "Main.dll");
            var sibling = Path.Combine(root, "Sibling.dll");
            await File.WriteAllBytesAsync(main, [1, 2, 3]);
            await File.WriteAllBytesAsync(sibling, [4, 5, 6]);
            workspace = new AdhocWorkspace();
            var project = workspace.AddProject("Catalog", LanguageNames.CSharp)
                .AddAnalyzerReference(new AnalyzerFileReference(main, new NoLoad()));
            var reads = 0;
            var denied = denySibling ? sibling : root;
            var observed = false;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (paths.Contains(denied, RoslynMcp.Roslyn.Helpers.FileSystemPath.Comparer))
                {
                    observed = true;
                    throw new UnauthorizedAccessException("Controlled analyzer catalog denial.");
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct)
            {
                reads++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                AnalyzerInputCatalog.CaptureAsync(project.Solution, [], Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            Assert.IsTrue(observed);
            Assert.AreEqual(0, reads, "Directory discovery and the complete exact catalog must be approved before reading bytes.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Denied analyzer catalog fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task Receipt_UsesCurrentAuthorizationBeforeLockedInputAndDetectsNewSibling()
    {
        var root = Path.Combine(TestTempRoot.Current, "catalog-current-" + Guid.NewGuid().ToString("N"));
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var main = Path.Combine(root, "Main.dll");
            await File.WriteAllBytesAsync(main, [1, 2, 3]);
            workspace = new AdhocWorkspace();
            var project = workspace.AddProject("Catalog", LanguageNames.CSharp)
                .AddAnalyzerReference(new AnalyzerFileReference(main, new NoLoad()));
            var policy = new OwnedTestWorkspacePathPolicy();
            Task Allow(IReadOnlyList<string> paths, CancellationToken ct) => policy.ValidateAsync("catalog", paths, ct);
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var catalog = await AnalyzerInputCatalog.CaptureAsync(project.Solution, [], Allow, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await catalog.AssertCurrentAsync(Allow, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            var reads = 0;
            Task Deny(IReadOnlyList<string> paths, CancellationToken ct) => throw new UnauthorizedAccessException("Current catalog grant revoked.");
            Task<byte[]?> ForbiddenRead(string path, CancellationToken ct) { reads++; throw new AssertFailedException("Read before current authorization."); }
            using (var locked = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.None))
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => catalog.AssertCurrentAsync(Deny, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, ForbiddenRead, digestToken), CancellationToken.None));
            Assert.AreEqual(0, reads);
            var sibling = Path.Combine(root, "New.dll");
            await File.WriteAllBytesAsync(sibling, [4, 5, 6]);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => catalog.AssertCurrentAsync(Allow, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(sibling));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Current analyzer catalog fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class NoLoad : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }
        public System.Reflection.Assembly LoadFromPath(string fullPath) => throw new AssertFailedException("Catalog capture cannot load an analyzer.");
    }
}
