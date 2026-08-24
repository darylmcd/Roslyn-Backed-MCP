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
            TestTempRoot.Current, "PersistentCompositeStorage", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Teardown()
    {
        TestFixtureFileSystem.DeleteDirectoryIfExists(_root);
    }

    [TestMethod]
    public void Write_ThenTryRead_RoundTripsEntry()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry(
            "ws-1", 3, "sample composite",
            new[] { new CompositeFileMutation(@"C:\proj\File.cs", "// updated", false) },
            DateTime.UtcNow);

        var token = Guid.NewGuid().ToString("N");
        store.Write(token, entry);
        var read = store.TryRead(token);

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
    public void TryRead_DirectoryEnumerationThrowsIOException_ReturnsNull()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new IOException("Injected enumeration race.")),
            File.GetLastWriteTimeUtc);

        Assert.IsNull(
            store.TryRead(Guid.NewGuid().ToString("N")),
            "A directory removed during lazy enumeration must be treated as a cache miss.");
    }

    [TestMethod]
    public void TryRead_LastWriteTimeThrowsIOException_ReturnsNull()
    {
        var token = Guid.NewGuid().ToString("N");
        var versionDirectory = Path.Combine(_root, "1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, token + ".json"), "{}");
        var timestampRead = false;
        var store = CreateStore(
            Directory.EnumerateDirectories,
            _ =>
            {
                timestampRead = true;
                throw new IOException("Injected last-write-time race.");
            });

        Assert.IsNull(
            store.TryRead(token),
            "A file removed between existence and timestamp checks must be treated as a cache miss.");
        Assert.IsTrue(timestampRead, "The test must reach the timestamp operation before asserting the race policy.");
    }

    [TestMethod]
    public void TryRead_DirectoryEnumerationThrowsUnauthorizedAccess_Propagates()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new UnauthorizedAccessException("Injected permissions failure.")),
            File.GetLastWriteTimeUtc);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            store.TryRead(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public void TryReadAndDelete_PathTraversalToken_CannotAccessOutsideRoot()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var versionDirectory = Path.Combine(_root, "1");
        Directory.CreateDirectory(versionDirectory);

        var outsidePath = Path.Combine(
            Directory.GetParent(_root)!.FullName,
            $"outside-{Guid.NewGuid():N}.json");
        File.WriteAllText(outsidePath, """{"workspaceId":"outside"}""");

        try
        {
            var traversalToken = $"../../{Path.GetFileNameWithoutExtension(outsidePath)}";

            Assert.IsNull(store.TryRead(traversalToken));
            store.Delete(traversalToken);

            Assert.IsTrue(
                File.Exists(outsidePath),
                "An invalid token must be rejected before file-system access.");
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [TestMethod]
    public void Write_InvalidToken_ThrowsBeforeCreatingVersionDirectory()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry(
            "ws-1",
            7,
            "invalid token",
            [],
            DateTime.UtcNow);

        Assert.ThrowsExactly<ArgumentException>(() => store.Write("../escape", entry));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "7")));
    }

    [TestMethod]
    public void Write_FinalMoveFails_RemovesTemporaryPayload()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        var versionDirectory = Path.Combine(_root, "7");
        var destinationPath = Path.Combine(versionDirectory, token + ".json");
        Directory.CreateDirectory(destinationPath);
        var entry = new CompositePreviewStore.Entry(
            "ws-1",
            7,
            "failed atomic move",
            [],
            DateTime.UtcNow);

        Exception? writeFailure = null;
        try
        {
            store.Write(token, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            writeFailure = ex;
        }

        Assert.IsNotNull(
            writeFailure,
            "The destination-directory collision must make the final atomic move fail.");
        Assert.IsFalse(
            File.Exists(destinationPath + ".tmp"),
            "A failed atomic move must not leave an orphaned temporary payload.");
    }

    private PersistentCompositeStorage CreateStore(
        Func<string, IEnumerable<string>> enumerateDirectoriesForRead,
        Func<string, DateTime> getLastWriteTimeUtc) =>
        new(
            _root,
            TimeSpan.FromMinutes(5),
            logger: null,
            enumerateDirectoriesForRead: enumerateDirectoriesForRead,
            getLastWriteTimeUtc: getLastWriteTimeUtc);

    private static IEnumerable<string> ThrowOnMoveNext(Exception exception)
    {
        yield return Throw(exception);
    }

    private static string Throw(Exception exception) => throw exception;
}
