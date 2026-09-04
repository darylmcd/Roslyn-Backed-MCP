using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Services;

/// <summary>
/// Direct unit tests for <see cref="PersistentCompositeStorage"/> cross-process token redemption.
/// Expected contention and disappearing paths are cache misses; unrelated I/O and permission
/// failures remain visible so callers fail closed.
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
    public void Write_ThenTryClaim_ConsumesAndRoundTripsEntry()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry(
            "ws-1", 3, "sample composite",
            new[] { new CompositeFileMutation(@"C:\proj\File.cs", "// updated", false) },
            DateTime.UtcNow);

        var token = Guid.NewGuid().ToString("N");
        store.Write(token, entry);
        var read = store.TryClaim(token);

        Assert.IsNotNull(read, "A freshly-written, unexpired entry must round-trip through TryClaim.");
        Assert.AreEqual("ws-1", read!.WorkspaceId);
        Assert.AreEqual(3, read.WorkspaceVersion);
        Assert.AreEqual("sample composite", read.Description);
        Assert.AreEqual(1, read.Mutations.Count);
        Assert.AreEqual(@"C:\proj\File.cs", read.Mutations[0].FilePath);
        Assert.AreEqual("// updated", read.Mutations[0].UpdatedContent);
        Assert.IsNull(store.TryClaim(token), "A claimed persistent token must not be redeemable twice.");
    }

    [TestMethod]
    public void TryClaim_MissingToken_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        Assert.IsNull(store.TryClaim("no-such-token"));
    }

    [TestMethod]
    public void TryRead_CompatibilityEntryPoint_UsesOneTimeClaimSemantics()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(
            token,
            new CompositePreviewStore.Entry("ws-compat", 1, "compatibility", [], DateTime.UtcNow));

        Assert.IsNotNull(store.TryRead(token));
        Assert.IsNull(store.TryRead(token));
    }

    [TestMethod]
    public void TryClaim_DirectoryEnumerationThrowsIOException_ReturnsNull()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new IOException("Injected enumeration race.")),
            File.GetLastWriteTimeUtc);

        Assert.IsNull(
            store.TryClaim(Guid.NewGuid().ToString("N")),
            "A directory removed during lazy enumeration must be treated as a cache miss.");
    }

    [TestMethod]
    public void TryClaim_LastWriteTimeThrowsIOException_Propagates()
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

        Assert.ThrowsExactly<IOException>(() => store.TryClaim(token));
        Assert.IsTrue(timestampRead, "The test must reach the timestamp operation before asserting the race policy.");
    }

    [TestMethod]
    public void TryClaim_DirectoryEnumerationThrowsUnauthorizedAccess_Propagates()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new UnauthorizedAccessException("Injected permissions failure.")),
            File.GetLastWriteTimeUtc);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            store.TryClaim(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public void TryClaimAndDelete_PathTraversalToken_CannotAccessOutsideRoot()
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

            Assert.IsNull(store.TryClaim(traversalToken));
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

    [TestMethod]
    public async Task TryClaim_TwoStorageInstances_RedeemExactlyOnceAsync()
    {
        var token = Guid.NewGuid().ToString("N");
        var writer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        writer.Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-race",
                11,
                "atomic claim",
                [new CompositeFileMutation(@"C:\proj\Claim.cs", "// claimed")],
                DateTime.UtcNow));

        var first = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var second = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        using var start = new ManualResetEventSlim();
        var claims = new[]
        {
            Task.Run(() => { start.Wait(); return first.TryClaim(token); }),
            Task.Run(() => { start.Wait(); return second.TryClaim(token); }),
        };

        start.Set();
        var results = await Task.WhenAll(claims);

        Assert.AreEqual(1, results.Count(result => result is not null));
        Assert.AreEqual(1, results.Count(result => result is null));
        Assert.AreEqual("ws-race", results.Single(result => result is not null)!.WorkspaceId);
    }

    [TestMethod]
    public void TryClaim_LockCollisionAfterWinnerCleanup_ReturnsNull()
    {
        var token = Guid.NewGuid().ToString("N");
        var writer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        writer.Write(
            token,
            new CompositePreviewStore.Entry("ws-race", 11, "atomic claim", [], DateTime.UtcNow));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            createClaimLock: lockPath =>
            {
                File.Delete(lockPath[..^".lock".Length]);
                throw new IOException("Injected lock collision after the winner removed its artifacts.");
            });

        Assert.IsNull(
            store.TryClaim(token),
            "A loser must remain a cache miss after the winner removes the lock before exception filtering.");
    }

    [TestMethod]
    public void TryClaim_UnrelatedLockCreationIOException_Propagates()
    {
        var token = Guid.NewGuid().ToString("N");
        var writer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        writer.Write(
            token,
            new CompositePreviewStore.Entry("ws-io", 11, "I/O failure", [], DateTime.UtcNow));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            createClaimLock: _ => throw new IOException("Injected storage fault."));

        Assert.ThrowsExactly<IOException>(() => store.TryClaim(token));
    }

    [TestMethod]
    public void Delete_DirectoryEnumerationThrowsIOException_IsIdempotent()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new IOException("Injected delete enumeration race.")),
            File.GetLastWriteTimeUtc);

        store.Delete(Guid.NewGuid().ToString("N"));
    }

    [TestMethod]
    public void Delete_DirectoryEnumerationThrowsUnauthorizedAccess_Propagates()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new UnauthorizedAccessException("Injected delete permissions failure.")),
            File.GetLastWriteTimeUtc);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            store.Delete(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    public void TryClaim_ClaimDeletionFails_PropagatesBeforeReturningPayload()
    {
        var token = Guid.NewGuid().ToString("N");
        var writer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        writer.Write(
            token,
            new CompositePreviewStore.Entry("ws-delete", 1, "delete failure", [], DateTime.UtcNow));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            _ => throw new UnauthorizedAccessException("Injected claim cleanup denial."));

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => store.TryClaim(token));
    }

    [TestMethod]
    public void Constructor_ExpiredAbandonedClaim_RemovesClaim()
    {
        var versionDirectory = Path.Combine(_root, "1");
        Directory.CreateDirectory(versionDirectory);
        var claimPath = Path.Combine(
            versionDirectory,
            Guid.NewGuid().ToString("N") + ".json.claim");
        File.WriteAllText(claimPath, "{}");
        File.SetLastWriteTimeUtc(claimPath, DateTime.UtcNow - TimeSpan.FromMinutes(10));

        _ = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));

        Assert.IsFalse(
            File.Exists(claimPath),
            "Startup recovery must remove expired claims abandoned by a terminated process.");
    }

    private PersistentCompositeStorage CreateStore(
        Func<string, IEnumerable<string>> enumerateDirectoriesForRead,
        Func<string, DateTime> getLastWriteTimeUtc,
        Action<string>? deleteFile = null,
        Func<string, FileStream>? createClaimLock = null) =>
        new(
            _root,
            TimeSpan.FromMinutes(5),
            logger: null,
            enumerateDirectoriesForRead: enumerateDirectoriesForRead,
            getLastWriteTimeUtc: getLastWriteTimeUtc,
            deleteFile: deleteFile ?? File.Delete,
            createClaimLock: createClaimLock ?? CreateClaimLock);

    private static FileStream CreateClaimLock(string lockPath) =>
        new(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

    private static IEnumerable<string> ThrowOnMoveNext(Exception exception)
    {
        yield return Throw(exception);
    }

    private static string Throw(Exception exception) => throw exception;
}
