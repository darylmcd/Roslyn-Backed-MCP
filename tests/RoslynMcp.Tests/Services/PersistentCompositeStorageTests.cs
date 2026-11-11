using System.Text.Json.Nodes;
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
            new[] { new CompositeFileMutation(TestPath("File.cs"), "// updated", CompositeFileMutationMode.Edit) },
            DateTime.UtcNow, Identity, [AbsentFingerprint("File.cs")]);

        var token = Guid.NewGuid().ToString("N");
        store.Write(token, entry);
        var read = store.TryClaim(token, Admit);

        Assert.IsNotNull(read, "A freshly-written, unexpired entry must round-trip through TryClaim.");
        Assert.AreEqual("ws-1", read!.WorkspaceId);
        Assert.AreEqual(3, read.WorkspaceVersion);
        Assert.AreEqual("sample composite", read.Description);
        Assert.AreEqual(1, read.Mutations.Count);
        Assert.AreEqual(TestPath("File.cs"), read.Mutations[0].FilePath);
        Assert.AreEqual("// updated", read.Mutations[0].UpdatedContent);
        Assert.AreEqual(CompositeFileMutationMode.Edit, read.Mutations[0].Mode);
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
    public void Write_PersistsFormat4WithPortableIdentity()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        store.Write(token, new CompositePreviewStore.Entry("ws-fmt", 5, "format", [], DateTime.UtcNow, Identity, []));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PayloadPath(token)));
        Assert.AreEqual(4, json.RootElement.GetProperty("FormatVersion").GetInt32());
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
    public void Format4_RoundTripsSourceFingerprints_ThroughPeekAndClaim()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = Guid.NewGuid().ToString("N");
        var existing = new CompositeSourceFingerprint(TestPath("Existing.cs"), true, new string('a', 64));
        var absent = new CompositeSourceFingerprint(TestPath("Created.cs"), false, null);
        store.Write(
            token,
            new CompositePreviewStore.Entry(
                "ws-fp", 1, "fingerprints",
                [new CompositeFileMutation(existing.FilePath, "// a", CompositeFileMutationMode.Edit), new CompositeFileMutation(absent.FilePath, "// b", CompositeFileMutationMode.Create)],
                DateTime.UtcNow, Identity, [existing, absent]));

        var peeked = store.TryPeek(token);
        var claimed = store.TryClaim(token, Admit);

        CollectionAssert.AreEqual(new[] { existing, absent }, peeked!.SourceFingerprints!.ToArray());
        CollectionAssert.AreEqual(new[] { existing, absent }, claimed!.SourceFingerprints!.ToArray());
        CollectionAssert.AreEqual(new[] { CompositeFileMutationMode.Edit, CompositeFileMutationMode.Create },
            claimed.Mutations.Select(mutation => mutation.Mode).ToArray());
    }

    [TestMethod]
    [DataRow("extra", DisplayName = "fingerprint outside the write set")]
    [DataRow("missing", DisplayName = "write target without a fingerprint")]
    [DataRow("duplicate", DisplayName = "duplicate fingerprint path")]
    [DataRow("null", DisplayName = "missing fingerprint list")]
    [DataRow("null entry", DisplayName = "null fingerprint")]
    [DataRow("blank path", DisplayName = "blank fingerprint path")]
    [DataRow("missing exists", DisplayName = "missing existence")]
    [DataRow("missing hash", DisplayName = "existing file without a hash")]
    [DataRow("invalid hash", DisplayName = "hash has wrong format")]
    [DataRow("absent with hash", DisplayName = "absent file with a hash")]
    [DataRow("create with hash", DisplayName = "create mutation with existing fingerprint")]
    [DataRow("missing mode", DisplayName = "mutation without explicit mode")]
    [DataRow("unknown mode", DisplayName = "mutation with unknown mode")]
    public void Format4_UnsafeMutationAndFingerprintSetsAreUnreadable(string scenario)
    {
        var path = TestPath("Written.cs");
        var hash = new string('a', 64);
        var current = new JsonObject { ["FilePath"] = path, ["Exists"] = false };
        var mutation = new JsonObject
        {
            ["FilePath"] = path,
            ["UpdatedContent"] = "// a",
            ["Mode"] = (int)CompositeFileMutationMode.Edit,
        };
        var fingerprints = new JsonArray(current);
        switch (scenario)
        {
            case "extra":
                fingerprints.Add(new JsonObject { ["FilePath"] = TestPath("Other.txt"), ["Exists"] = false });
                break;
            case "missing":
                fingerprints.Clear();
                break;
            case "duplicate":
                fingerprints.Add(current.DeepClone());
                break;
            case "null entry":
                fingerprints[0] = null;
                break;
            case "blank path":
                current["FilePath"] = " ";
                break;
            case "missing exists":
                current.Remove("Exists");
                break;
            case "missing hash":
                current["Exists"] = true;
                break;
            case "invalid hash":
                current["Exists"] = true;
                current["Sha256"] = "invalid";
                break;
            case "absent with hash":
                current["Sha256"] = hash;
                break;
            case "create with hash":
                current["Exists"] = true;
                current["Sha256"] = hash;
                mutation["Mode"] = (int)CompositeFileMutationMode.Create;
                break;
            case "missing mode":
                mutation.Remove("Mode");
                break;
            case "unknown mode":
                mutation["Mode"] = 100;
                break;
        }

        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(new JsonObject
        {
            ["FormatVersion"] = 4,
            ["WorkspaceId"] = "ws",
            ["WorkspaceIdentity"] = Identity,
            ["Mutations"] = new JsonArray(mutation),
            ["SourceFingerprints"] = scenario == "null" ? null : fingerprints,
        }.ToJsonString());

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, _ => throw new AssertFailedException("The invalid record reached admission.")));
    }

    /// <summary>
    /// On Windows <c>Path.GetFullPath</c> rejects an overlong path with <see cref="IOException"/>.
    /// A tampered record must produce an unreadable result.
    /// </summary>
    [TestMethod]
    [DataRow("identity", DisplayName = "overlong workspace identity")]
    [DataRow("fingerprint", DisplayName = "overlong fingerprint path")]
    [DataRow("mutation", DisplayName = "overlong mutation path with a fingerprint")]
    public void Format4_OverlongPath_IsUnreadable_ForPeekAndClaim(string where)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Only Windows rejects an overlong path in Path.GetFullPath; the NUL-character test covers the same tamper shape on every OS.");
        }

        var overlong = @"C:\" + new string('a', 40_000);
        AssertTamperedPathIsUnreadable(where, overlong, overlong);
    }

    /// <summary>
    /// A NUL character is rejected by <c>Path.GetFullPath</c> (<see cref="ArgumentException"/>) on every OS,
    /// so a tampered identity, fingerprint or mutation path is an unreadable record on Windows, Linux and macOS.
    /// </summary>
    [TestMethod]
    [DataRow("identity", DisplayName = "NUL in workspace identity")]
    [DataRow("fingerprint", DisplayName = "NUL in fingerprint path")]
    [DataRow("mutation", DisplayName = "NUL in mutation path with a fingerprint")]
    public void Format4_PathWithNulCharacter_IsUnreadable_ForPeekAndClaim(string where) =>
        AssertTamperedPathIsUnreadable(where, TestPath("x.cs") + '\0', Identity + '\0');

    private void AssertTamperedPathIsUnreadable(string where, string tampered, string tamperedIdentity)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var identity = where == "identity" ? tamperedIdentity : Identity;
        var mutationPath = where == "mutation" ? tampered : TestPath("x.cs");
        var fingerprintPath = where == "fingerprint" ? tampered : mutationPath;
        var token = WriteRaw(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                FormatVersion = 4,
                WorkspaceId = "ws",
                WorkspaceIdentity = identity,
                Mutations = new[] { new { FilePath = mutationPath, UpdatedContent = "// x", Mode = CompositeFileMutationMode.Edit } },
                SourceFingerprints = new[] { new { FilePath = fingerprintPath, Exists = false } },
            }));
        var admitted = false;

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; }));
        Assert.IsFalse(admitted);
    }

    [TestMethod]
    [DataRow("", DisplayName = "format 1 with implicit format version")]
    [DataRow("\"FormatVersion\":2,", DisplayName = "format 2")]
    [DataRow("\"FormatVersion\":3,", DisplayName = "format 3")]
    public void TryClaim_LegacyFormat_RefusesAndKeepsPayload(string formatProperty)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw("{" + formatProperty + "\"WorkspaceId\":\"legacy\",\"Mutations\":[]}");

        Assert.IsNull(store.TryPeek(token));
        Assert.IsNull(store.TryClaim(token, _ => throw new AssertFailedException("A legacy record reached admission.")));
        Assert.IsTrue(File.Exists(PayloadPath(token)));
        Assert.IsEmpty(ClaimArtifacts());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("relative/Sample.slnx")]
    [DataRow("Sample.slnx")]
    [DataRow("../Sample.slnx")]
    public void Format4_WithMissingOrRelativeIdentity_IsUnreadable(string? identity)
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(System.Text.Json.JsonSerializer.Serialize(new
        {
            FormatVersion = 4,
            WorkspaceId = "ws",
            WorkspaceIdentity = identity,
            Mutations = Array.Empty<object>(),
            SourceFingerprints = Array.Empty<object>(),
        }));

        Assert.IsNull(store.TryPeek(token), "A relative identity would resolve against the process cwd.");
        Assert.IsNull(store.TryClaim(token, _ => throw new AssertFailedException("An invalid identity reached admission.")));
    }

    [TestMethod]
    public void TryClaim_FormatVersionAbove4_RefusesAndKeepsPayload()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"FormatVersion":5,"WorkspaceId":"ws","Mutations":{"FutureMode":"future shape"}}""");
        var admitted = false;

        var claimed = store.TryClaim(token, entry => { admitted = true; return entry.WorkspaceId; });

        Assert.IsNull(claimed, "A record written by a newer server must be refused.");
        Assert.IsFalse(admitted, "The admit callback must never see a record this reader cannot interpret.");
        Assert.IsTrue(File.Exists(PayloadPath(token)), "The refused payload must stay for a newer reader.");
        Assert.IsEmpty(ClaimArtifacts(),
            "No claim or lock artifact may be left behind.");
    }

    [TestMethod]
    public void TryPeek_FormatVersionAbove4_ReturnsNull()
    {
        var store = new PersistentCompositeStorage(_root, TimeSpan.FromMinutes(5));
        var token = WriteRaw(
            """{"FormatVersion":5,"WorkspaceId":"ws","Mutations":{"FutureMode":"future shape"}}""");

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
                [new CompositeFileMutation(TestPath("Claim.cs"), "// claimed", CompositeFileMutationMode.Edit)],
                DateTime.UtcNow, Identity, [AbsentFingerprint("Claim.cs")]));

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
                "ws-peek", 4, "peek", [new CompositeFileMutation(TestPath("Peek.cs"), "// peeked", CompositeFileMutationMode.Edit)], DateTime.UtcNow, Identity, [AbsentFingerprint("Peek.cs")]));

        var first = store.TryPeek(token);
        var second = store.TryPeek(token);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual("ws-peek", first.WorkspaceId);
        Assert.AreEqual(TestPath("Peek.cs"), first.Mutations.Single().FilePath);
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
    [DataRow("""{"FormatVersion":4}""")]
    [DataRow("not json")]
    [DataRow("""{"FormatVersion":4,"WorkspaceId":"ws","Mutations":null}""")]
    [DataRow("""{"FormatVersion":4,"WorkspaceId":"ws","Mutations":[null]}""")]
    [DataRow("""{"FormatVersion":4,"WorkspaceId":"ws","Mutations":[{"FilePath":" "}]}""")]
    [DataRow("""{"FormatVersion":4,"WorkspaceId":null,"Mutations":[{"FilePath":"C:\\x.cs"}]}""")]
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
                "ws-race", 1, "peek vs claim", [new CompositeFileMutation(TestPath("Race.cs"), "// race", CompositeFileMutationMode.Edit)], DateTime.UtcNow, Identity, [AbsentFingerprint("Race.cs")]));
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

    // Rooted on every platform: a persisted identity must be fully qualified.
    private static readonly string Identity = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "proj", "Sample.slnx"));

    private string TestPath(string name) => Path.Combine(_root, name);

    private CompositeSourceFingerprint AbsentFingerprint(string name) => new(TestPath(name), false, null);

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
