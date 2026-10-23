using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentReferenceSearchTests
{
    [TestMethod]
    [DataRow("earlier")]
    [DataRow("selected")]
    [DataRow("drift")]
    [DataRow("directory")]
    public async Task OfficialReferenceSearch_ApprovesEveryActualProbeAndFreezesEarlierAbsence(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentReferenceSearchTests), Guid.NewGuid().ToString("N"));
        var library = Path.Combine(root, "lib");
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(library);
            var name = "FixtureReference" + Guid.NewGuid().ToString("N") + ".dll";
            var selected = Path.Combine(library, name);
            var earlier = Path.Combine(root, name);
            await File.WriteAllBytesAsync(selected, [1, 2, 3]);
            var authorized = new HashSet<string>(FileSystemPath.Comparer);
            var reads = 0;
            var watchEarlierReads = false;
            var earlierReads = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (shape == "earlier" && FileSystemPath.Comparer.Equals(path, earlier)
                        || shape == "selected" && FileSystemPath.Comparer.Equals(path, selected))
                        throw new UnauthorizedAccessException("Synthetic metadata search denial.");
                    authorized.Add(path);
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct)
            {
                Assert.IsTrue(authorized.Contains(path), "Every physical reference candidate must be approved before an explicit read.");
                reads++;
                if (watchEarlierReads && FileSystemPath.Comparer.Equals(path, earlier)) earlierReads++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            var context = new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var parsed = await context.ParseAsync(["-nostdlib+", "-lib:lib", "-reference:FixtureAlias=" + name], CancellationToken.None);
            var native = new SourceFileResolver(parsed.Arguments.ReferencePaths, root).ResolveReference(name, baseFilePath: null);
            Assert.AreEqual(selected, native);
            if (shape is "earlier" or "selected")
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => context.ResolveReferencesAsync(parsed, [], CancellationToken.None));
                Assert.AreEqual(0, reads, "Neither earlier denied absence nor a denied selected file may be read.");
            }
            else
            {
                var resolved = await context.ResolveReferencesAsync(parsed, [], CancellationToken.None);
                Assert.AreEqual(selected, resolved.References.Single().Path);
                CollectionAssert.Contains(resolved.References.Single().Properties.Aliases.ToArray(), "FixtureAlias");
                Assert.IsTrue(resolved.Parsing.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, earlier) && !input.Exists));
                await resolved.Parsing.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
                watchEarlierReads = true;
                if (shape == "directory") Directory.CreateDirectory(earlier);
                else await File.WriteAllBytesAsync(earlier, [1, 2, 3]);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => resolved.Parsing.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
                Assert.AreEqual(0, earlierReads, "Changed input kinds must be refused before a byte read.");
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler reference search fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
