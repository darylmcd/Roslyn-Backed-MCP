using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Direct unit tests for <see cref="PersistentCompositeStorage"/> covering the cross-process
/// TOCTOU hardening in <c>TryRead</c> (<c>workspace-infra-resource-cleanup-hygiene</c>). The
/// store's whole purpose is cross-process token redemption, so a sibling process deleting a
/// version subdirectory (or the root) while <c>TryRead</c> is enumerating is a real scenario;
/// the enumeration must be treated as a cache miss rather than surfacing an uncaught
/// <see cref="DirectoryNotFoundException"/>/<see cref="IOException"/>.
/// </summary>
/// <remarks>
/// Each test uses an isolated cache root under <see cref="Path.GetTempPath"/> so concurrent test
/// runs don't poison each other.
/// </remarks>
[TestClass]
public sealed class PersistentCompositeStorageTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "RoslynMcpTests", "PersistentCompositeStorage", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Teardown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; a leaked temp dir under GetTempPath is harmless.
        }
    }

    [TestMethod]
    public void Write_ThenTryRead_RoundTripsEntry()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry(
            "ws-1", 3, "sample composite",
            new[] { new CompositeFileMutation(@"C:\proj\File.cs", "// updated", false) },
            DateTime.UtcNow);

        store.Write("token-abc", entry);
        var read = store.TryRead("token-abc");

        Assert.IsNotNull(read, "A freshly-written, unexpired entry must round-trip through TryRead.");
        Assert.AreEqual("ws-1", read!.WorkspaceId);
        Assert.AreEqual(3, read.WorkspaceVersion);
        Assert.AreEqual("sample composite", read.Description);
        Assert.AreEqual(1, read.Mutations.Count);
        Assert.AreEqual(@"C:\proj\File.cs", read.Mutations[0].FilePath);
        Assert.AreEqual("// updated", read.Mutations[0].UpdatedContent);
    }

    [TestMethod]
    public void TryRead_MissingToken_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        Assert.IsNull(store.TryRead("no-such-token"));
    }

    [TestMethod]
    public async Task TryRead_ConcurrentDirectoryDeletion_ReturnsNull_DoesNotThrow()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));

        // Repeatedly race TryRead's lazy Directory.EnumerateDirectories walk against a concurrent
        // recursive deletion of the whole root. Before the fix this threw an uncaught
        // DirectoryNotFoundException/IOException from the enumerator's MoveNext (or from the
        // File.GetLastWriteTimeUtc TTL stat). The assertion is timing-independent: every iteration
        // must return null without throwing, whichever way the race resolves.
        for (var iteration = 0; iteration < 200; iteration++)
        {
            Directory.CreateDirectory(_root);
            for (var version = 0; version < 40; version++)
            {
                var versionDir = Path.Combine(_root, version.ToString());
                Directory.CreateDirectory(versionDir);
                // A decoy file so the enumeration + per-subdir File.Exists probes take real time,
                // widening the window in which the background delete lands mid-walk.
                File.WriteAllText(Path.Combine(versionDir, "decoy.json"), "{}");
            }

            var deleter = Task.Run(() =>
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch
                {
                    // The delete itself may race the reader (sharing violation); irrelevant to
                    // what we're asserting, which is that the READER never throws.
                }
            });

            CompositePreviewStore.Entry? result = null;
            try
            {
                result = store.TryRead("token-under-race");
            }
            catch (Exception ex)
            {
                Assert.Fail($"TryRead threw during a concurrent directory deletion race: {ex}");
            }

            Assert.IsNull(result, "A token that was never written must read as a miss, even mid-race.");
            await deleter;
        }
    }
}
