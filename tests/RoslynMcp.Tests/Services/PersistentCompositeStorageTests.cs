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
    public void Write_InvalidTokenPublishesFormatWithoutIdentifier()
    {
        const string sentinel = "PRIVATE-PERSISTENT-TOKEN";
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry("PRIVATE-WORKSPACE", 1, "test", [], DateTime.UtcNow, Identity, []);
        var exception = Assert.Throws<ArgumentException>(() => store.Write(sentinel, entry));
        Assert.IsInstanceOfType<IPublicMessageException>(exception);
        using var envelope = System.Text.Json.JsonDocument.Parse(
            RoslynMcp.Host.Stdio.Tools.ToolErrorHandler.ClassifyAndFormat(exception, "apply_composite"));
        Assert.AreEqual("InvalidArgument", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("ArgumentException", envelope.RootElement.GetProperty("exceptionType").GetString());
        StringAssert.Contains(envelope.RootElement.GetProperty("message").GetString()!, "32 hexadecimal GUID characters");
        Assert.IsFalse(envelope.RootElement.GetRawText().Contains(sentinel, StringComparison.Ordinal));
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(_root));
    }

    [TestMethod]
    public void Write_ThenTryClaim_ConsumesAndRoundTripsEntry()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var entry = new CompositePreviewStore.Entry(
            "ws-1", 3, "sample composite",
            new[] { new CompositeFileMutation(@"C:\proj\File.cs", "// updated", false) },
            DateTime.UtcNow, Identity, []);

        var token = Guid.NewGuid().ToString("N");
        store.Write(token, entry);
        var read = store.TryClaim(token, Admit);

        Assert.IsNotNull(read, "A freshly-written, unexpired entry must round-trip through TryClaim.");
        Assert.AreEqual("ws-1", read!.WorkspaceId);
        Assert.AreEqual(3, read.WorkspaceVersion);
        Assert.AreEqual("sample composite", read.Description);
        Assert.AreEqual(1, read.Mutations.Count);
        Assert.AreEqual(@"C:\proj\File.cs", read.Mutations[0].FilePath);
        Assert.AreEqual("// updated", read.Mutations[0].UpdatedContent);
        Assert.IsNull(store.TryClaim(token, Admit), "A claimed persistent token must not be redeemable twice.");
    }

    [TestMethod]
    public void TryClaim_MissingToken_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        Assert.IsNull(store.TryClaim("no-such-token", Admit));
    }

    [TestMethod]
    public void TryClaim_SecondClaimAfterSuccess_IsAMiss()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(
            token,
            new CompositePreviewStore.Entry("ws-compat", 1, "compatibility", [], DateTime.UtcNow, Identity, []));

        Assert.IsNotNull(store.TryClaim(token, Admit));
        Assert.IsNull(store.TryClaim(token, Admit));
    }

    [TestMethod]
    public void Write_PersistsFormat3WithPortableIdentity()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(token, new CompositePreviewStore.Entry("ws-fmt", 5, "format", [], DateTime.UtcNow, Identity, []));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PayloadPath(token)));
        Assert.AreEqual(3, json.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.AreEqual(Identity, json.RootElement.GetProperty("WorkspaceIdentity").GetString());
        Assert.AreEqual("ws-fmt", json.RootElement.GetProperty("WorkspaceId").GetString(), "Kept for diagnostics.");
        Assert.AreEqual(5, json.RootElement.GetProperty("WorkspaceVersion").GetInt32(), "Kept for diagnostics.");
    }

    [TestMethod]
    public void Write_WithoutIdentity_IsRefusedBeforeAnyFileIsCreated()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));

        Assert.ThrowsExactly<PublicArgumentException>(() => store.Write(
            Guid.NewGuid().ToString("N"),
            new CompositePreviewStore.Entry("ws-none", 1, "no identity", [], DateTime.UtcNow, null, [])));
        Assert.IsEmpty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void Write_WithoutSourceFingerprints_IsRefusedBeforeAnyFileIsCreated()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));

        Assert.ThrowsExactly<PublicArgumentException>(() => store.Write(
            Guid.NewGuid().ToString("N"),
            new CompositePreviewStore.Entry("ws-none", 1, "no fingerprints", [], DateTime.UtcNow, Identity, null)));
        Assert.IsEmpty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [TestMethod]
    public void Format3_RoundTripsSourceFingerprints_ThroughPeekAndClaim()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        var existing = new CompositeSourceFingerprint(@"C:\proj\Existing.cs", true, new string('a', 64));
        var absent = new CompositeSourceFingerprint(@"C:\proj\Created.cs", false, null);
        store.Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-fp", 1, "fingerprints",
                [new CompositeFileMutation(existing.FilePath, "// a"), new CompositeFileMutation(absent.FilePath, "// b")],
                DateTime.UtcNow, Identity, [existing, absent]));

        var peeked = store.TryPeek(token);
        var claimed = store.TryClaim(token, Admit);

        CollectionAssert.AreEqual(new[] { existing, absent }, peeked!.SourceFingerprints!.ToArray());
        CollectionAssert.AreEqual(new[] { existing, absent }, claimed!.SourceFingerprints!.ToArray());
    }

    /// <summary>
    /// The shared directory is untrusted: a fingerprint for a path no mutation writes would make the
    /// redeeming host read (and hash, and report on) an arbitrary file, so such a record is unreadable.
    /// </summary>
    [TestMethod]
    public void Format3_FingerprintForAPathNoMutationWrites_IsUnreadable_ForPeekAndClaim()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-tamper", 1, "tamper",
                [new CompositeFileMutation(@"C:\proj\Written.cs", "// a")],
                DateTime.UtcNow, Identity,
                [new CompositeSourceFingerprint(@"C:\proj\Written.cs", false, null),
                 new CompositeSourceFingerprint(@"C:\secrets\Other.txt", false, null)]));
        var admitted = false;

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; }));
        Assert.IsFalse(admitted, "A tampered record must never reach session matching.");
    }

    [TestMethod]
    public void Format2Record_StillReads_WithNullFingerprints()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            "{\"FormatVersion\":2,\"WorkspaceId\":\"ws-v2\",\"WorkspaceIdentity\":" + System.Text.Json.JsonSerializer.Serialize(Identity)
            + ",\"Mutations\":[{\"FilePath\":\"C:/x.cs\",\"UpdatedContent\":\"// x\"}]}");

        var claimed = store.TryClaim(token, Admit);

        Assert.IsNotNull(claimed, "A format 2 record must still read.");
        Assert.IsNull(claimed.SourceFingerprints, "Format 2 predates fingerprints: it is redeemed unchecked.");
    }

    [TestMethod]
    [DataRow("null", DisplayName = "format 3 without a fingerprint list")]
    [DataRow("[null]", DisplayName = "null fingerprint")]
    [DataRow("""[{"FilePath":" ","Exists":false}]""", DisplayName = "blank fingerprint path")]
    [DataRow("""[{"FilePath":"C:/x.cs","Exists":true}]""", DisplayName = "existing file without a hash")]
    public void Format3_MalformedFingerprints_AreUnreadable_ForPeekAndClaim(string fingerprints)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            "{\"FormatVersion\":3,\"WorkspaceId\":\"ws\",\"WorkspaceIdentity\":" + System.Text.Json.JsonSerializer.Serialize(Identity)
            + ",\"Mutations\":[{\"FilePath\":\"C:/x.cs\"}],\"SourceFingerprints\":" + fingerprints + "}");

        Assert.IsNull(store.TryPeek(token), "A malformed fingerprint list must never reach a caller.");
        Assert.IsNull(store.TryClaim(token, Admit));
    }

    /// <summary>
    /// <c>Path.GetFullPath</c> throws <see cref="PathTooLongException"/> (an <see cref="IOException"/>) for an
    /// overlong path. The record is untrusted input, so that is an unreadable record, never an exception.
    /// </summary>
    [TestMethod]
    [DataRow("identity", DisplayName = "overlong workspace identity")]
    [DataRow("fingerprint", DisplayName = "overlong fingerprint path")]
    [DataRow("mutation", DisplayName = "overlong mutation path with a fingerprint")]
    public void Format3_OverlongPath_IsUnreadable_ForPeekAndClaim(string where)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var overlong = @"C:\" + new string('a', 40_000);
        var identity = where == "identity" ? overlong : Identity;
        var mutationPath = where == "mutation" ? overlong : @"C:\x.cs";
        var fingerprintPath = where == "fingerprint" ? overlong : mutationPath;
        var token = WriteRaw(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                FormatVersion = 3,
                WorkspaceId = "ws",
                WorkspaceIdentity = identity,
                Mutations = new[] { new { FilePath = mutationPath } },
                SourceFingerprints = new[] { new { FilePath = fingerprintPath, Exists = false } },
            }));
        var admitted = false;

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; }));
        Assert.IsFalse(admitted);
    }

    [TestMethod]
    public void TryClaim_FormatOneRecord_ReadsWithNullIdentity()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"WorkspaceId":"ws-v1","WorkspaceVersion":2,"Description":"legacy","Mutations":[{"FilePath":"C:/x.cs","UpdatedContent":"// x","DeleteFile":false}],"CreatedAt":"2026-11-06T00:00:00Z"}""");

        var claimed = store.TryClaim(token, Admit);

        Assert.IsNotNull(claimed, "A record without FormatVersion is format 1 and must still read.");
        Assert.AreEqual("ws-v1", claimed.WorkspaceId);
        Assert.IsNull(claimed.WorkspaceIdentity, "Format 1 carried only the creator's session id.");
    }

    [TestMethod]
    public void TryClaim_FormatTwoWithoutIdentity_IsUnreadable()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"FormatVersion":2,"WorkspaceId":"ws","Mutations":[{"FilePath":"C:/x.cs"}]}""");

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, Admit));
    }

    [TestMethod]
    [DataRow("relative/Sample.slnx")]
    [DataRow("Sample.slnx")]
    [DataRow("../Sample.slnx")]
    public void FormatTwoWithRelativeIdentity_IsUnreadable_ForPeekAndClaim(string identity)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            "{\"FormatVersion\":2,\"WorkspaceId\":\"ws\",\"WorkspaceIdentity\":\"" + identity
            + "\",\"Mutations\":[{\"FilePath\":\"C:/x.cs\"}]}");
        var admitted = false;

        Assert.IsNull(store.TryPeek(token), "A relative identity would resolve against the process cwd.");
        Assert.IsNull(store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; }));
        Assert.IsFalse(admitted, "An unusable identity must never reach session matching.");
    }

    [TestMethod]
    public void TryClaim_FormatVersionAbove3_RefusesAndKeepsPayload()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"FormatVersion":4,"WorkspaceId":"ws","WorkspaceIdentity":"C:/a.slnx","Mutations":[{"FilePath":"C:/x.cs"}]}""");
        var admitted = false;

        var claimed = store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; });

        Assert.IsNull(claimed, "A record written by a newer server must be refused.");
        Assert.IsFalse(admitted, "The admit callback must never see a record this reader cannot interpret.");
        Assert.IsTrue(File.Exists(PayloadPath(token)), "The refused payload must stay for a newer reader.");
        Assert.IsEmpty(ClaimArtifacts(),
            "No claim or lock artifact may be left behind.");
    }

    [TestMethod]
    public void TryPeek_FormatVersionAbove3_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"FormatVersion":4,"WorkspaceId":"ws","WorkspaceIdentity":"C:/a.slnx","Mutations":[{"FilePath":"C:/x.cs"}]}""");

        Assert.IsNull(store.TryPeek(token), "An uninterpretable write set is unknown, so the peek fails closed.");
    }

    [TestMethod]
    public void TryClaim_AdmitThrows_KeepsPayloadAndReleasesLock()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(token, new CompositePreviewStore.Entry("ws-refuse", 1, "refusal", [], DateTime.UtcNow, Identity, []));
        var payloadSeenByAdmit = false;

        Assert.ThrowsExactly<InvalidOperationException>(() => store.TryClaim(token, _ =>
        {
            payloadSeenByAdmit = File.Exists(PayloadPath(token));
            throw new InvalidOperationException("no matching session");
        }));

        Assert.IsTrue(payloadSeenByAdmit, "Admission must run before the payload is moved.");
        Assert.IsTrue(File.Exists(PayloadPath(token)), "A refused admission must not consume the token.");
        Assert.IsEmpty(ClaimArtifacts(),
            "The claim lock must be released on refusal.");
        var second = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        Assert.IsNotNull(second.TryClaim(token, Admit), "A later claimer must still win the token.");
    }

    [TestMethod]
    public void TryClaim_AdmitResult_ReplacesWorkspaceIdWithLocalSession()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(token, new CompositePreviewStore.Entry("creator-session", 1, "remap", [], DateTime.UtcNow, Identity, []));

        var claimed = store.TryClaim(token, _ => "local-session");

        Assert.AreEqual("local-session", claimed!.WorkspaceId);
        Assert.AreEqual(Path.GetFullPath(Identity), claimed.WorkspaceIdentity);
    }

    [TestMethod]
    public void TryClaim_DirectoryEnumerationThrowsIOException_ReturnsNull()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new IOException("Injected enumeration race.")),
            File.GetLastWriteTimeUtc);

        Assert.IsNull(
            store.TryClaim(Guid.NewGuid().ToString("N"), Admit),
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

        Assert.ThrowsExactly<IOException>(() => store.TryClaim(token, Admit));
        Assert.IsTrue(timestampRead, "The test must reach the timestamp operation before asserting the race policy.");
    }

    [TestMethod]
    public void TryClaim_DirectoryEnumerationThrowsUnauthorizedAccess_Propagates()
    {
        var store = CreateStore(
            _ => ThrowOnMoveNext(new UnauthorizedAccessException("Injected permissions failure.")),
            File.GetLastWriteTimeUtc);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
            store.TryClaim(Guid.NewGuid().ToString("N"), Admit));
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

            Assert.IsNull(store.TryClaim(traversalToken, Admit));
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
            DateTime.UtcNow, Identity, []);

        Assert.ThrowsExactly<PublicArgumentException>(() => store.Write("../escape", entry));
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
            DateTime.UtcNow, Identity, []);

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
                DateTime.UtcNow, Identity, []));

        var first = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var second = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        using var start = new ManualResetEventSlim();
        var claims = new[]
        {
            Task.Run(() => { start.Wait(); return first.TryClaim(token, Admit); }),
            Task.Run(() => { start.Wait(); return second.TryClaim(token, Admit); }),
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
            new CompositePreviewStore.Entry("ws-race", 11, "atomic claim", [], DateTime.UtcNow, Identity, []));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            createClaimLock: lockPath =>
            {
                File.Delete(lockPath[..^".lock".Length]);
                throw new IOException("Injected lock collision after the winner removed its artifacts.");
            });

        Assert.IsNull(
            store.TryClaim(token, Admit),
            "A loser must remain a cache miss after the winner removes the lock before exception filtering.");
    }

    [TestMethod]
    public void TryClaim_UnrelatedLockCreationIOException_Propagates()
    {
        var token = Guid.NewGuid().ToString("N");
        var writer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        writer.Write(
            token,
            new CompositePreviewStore.Entry("ws-io", 11, "I/O failure", [], DateTime.UtcNow, Identity, []));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            createClaimLock: _ => throw new IOException("Injected storage fault."));

        Assert.ThrowsExactly<IOException>(() => store.TryClaim(token, Admit));
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
            new CompositePreviewStore.Entry("ws-delete", 1, "delete failure", [], DateTime.UtcNow, Identity, []));
        var store = CreateStore(
            Directory.EnumerateDirectories,
            File.GetLastWriteTimeUtc,
            _ => throw new UnauthorizedAccessException("Injected claim cleanup denial."));

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => store.TryClaim(token, Admit));
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

    [TestMethod]
    public void TryPeek_ReturnsEntryWithoutConsumingIt()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-peek", 4, "peek", [new CompositeFileMutation(@"C:\proj\Peek.cs", "// peeked")], DateTime.UtcNow, Identity, []));

        var first = store.TryPeek(token);
        var second = store.TryPeek(token);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual("ws-peek", first.WorkspaceId);
        Assert.AreEqual(@"C:\proj\Peek.cs", first.Mutations.Single().FilePath);
        Assert.IsNotNull(store.TryClaim(token, Admit), "A peek must leave the one-time token claimable.");
        Assert.IsNull(store.TryPeek(token), "A claimed token is no longer peekable.");
    }

    [TestMethod]
    public void TryPeek_MissingInvalidOrExpiredToken_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var expired = Guid.NewGuid().ToString("N");
        store.Write(expired, new CompositePreviewStore.Entry("ws-old", 1, "old", [], DateTime.UtcNow, Identity, []));
        File.SetLastWriteTimeUtc(
            Directory.EnumerateFiles(_root, expired + ".json", SearchOption.AllDirectories).Single(),
            DateTime.UtcNow - TimeSpan.FromMinutes(10));

        Assert.IsNull(store.TryPeek(Guid.NewGuid().ToString("N")));
        Assert.IsNull(store.TryPeek("../not-a-token"));
        Assert.IsNull(store.TryPeek(expired));
    }

    [TestMethod]
    public void TryPeek_ClaimInProgress_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(token, new CompositePreviewStore.Entry("ws-locked", 1, "locked", [], DateTime.UtcNow, Identity, []));
        var payload = Directory.EnumerateFiles(_root, token + ".json", SearchOption.AllDirectories).Single();
        File.WriteAllText(payload + ".lock", string.Empty);

        Assert.IsNull(store.TryPeek(token), "A lock sibling means a claimant is consuming the payload.");
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("not json")]
    [DataRow("""{"WorkspaceId":"ws","Mutations":null}""")]
    [DataRow("""{"WorkspaceId":"ws","Mutations":[null]}""")]
    [DataRow("""{"WorkspaceId":"ws","Mutations":[{"FilePath":" "}]}""")]
    [DataRow("""{"WorkspaceId":null,"Mutations":[{"FilePath":"C:\\x.cs"}]}""")]
    public void IncompletePersistedPayload_IsUnreadable_ForPeekAndClaim(string json)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        var versionDirectory = Path.Combine(_root, "1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, token + ".json"), json);

        Assert.IsNull(store.TryPeek(token), "A payload missing a required member must never reach a caller.");
        Assert.IsNull(store.TryClaim(token, Admit), "The claim path shares the parser, so it must reject the same payloads.");
    }

    /// <summary>
    /// A peek must never make a legitimate claimant fail: the winning host renames the payload with
    /// <c>File.Move</c>, which throws a raw <see cref="IOException"/> on Windows while another handle
    /// is open without <see cref="FileShare.Delete"/>. The claim runs from inside the peek's
    /// timestamp probe, i.e. while the peek still holds the payload open.
    /// </summary>
    [TestMethod]
    public void TryPeek_ClaimWhilePeekHoldsPayloadOpen_ClaimStillSucceeds()
    {
        var token = Guid.NewGuid().ToString("N");
        new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5)).Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-race", 1, "peek vs claim", [new CompositeFileMutation(@"C:\proj\Race.cs", "// race")], DateTime.UtcNow, Identity, []));
        var claimer = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        CompositePreviewStore.Entry? claimed = null;
        Exception? claimFailure = null;
        var peeker = CreateStore(
            Directory.EnumerateDirectories,
            path =>
            {
                try
                {
                    claimed = claimer.TryClaim(token, Admit);
                }
                catch (Exception ex)
                {
                    claimFailure = ex;
                }

                return DateTime.UtcNow;
            });

        var peeked = peeker.TryPeek(token);

        Assert.IsNull(claimFailure, "The claimant must not fail while a peek holds the payload: " + claimFailure);
        Assert.IsNotNull(claimed, "The claimant must win the one-time token.");
        Assert.IsNotNull(peeked, "The peek read the payload before the claim consumed it.");
        Assert.IsNull(peeker.TryPeek(token), "Once claimed, the token is gone.");
    }

    // Rooted on every platform: a format-2 identity must be fully qualified.
    private static readonly string Identity = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj", "Sample.slnx"));

    private static string Admit(CompositePreviewStore.Entry entry) => entry.WorkspaceId;

    private string PayloadPath(string token) =>
        Directory.EnumerateFiles(_root, token + ".json", SearchOption.AllDirectories).Single();

    // Not a "<token>.json.*" glob: on Windows that pattern also matches "<token>.json" itself.
    private string[] ClaimArtifacts() =>
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".lock", StringComparison.Ordinal) || path.EndsWith(".claim", StringComparison.Ordinal))
            .ToArray();

    private string WriteRaw(string json)
    {
        var token = Guid.NewGuid().ToString("N");
        var versionDirectory = Path.Combine(_root, "1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, token + ".json"), json);
        return token;
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
