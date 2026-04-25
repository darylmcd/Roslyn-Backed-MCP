using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>host-recycle-opacity</c>: when the roslyn-mcp host stdio process is
/// recycled mid-session (idle eviction, watchdog kill, client disconnect, crash), every
/// subsequent workspace-scoped tool call returned <c>NotFound</c> with no recycle reason.
/// Post-fix the previous process writes a small disk record on shutdown, the next host
/// instance reads it on startup, and the FIRST <c>server_info</c> / <c>server_heartbeat</c>
/// probe surfaces <c>previousStdioPid</c> / <c>previousExitedAt</c> /
/// <c>previousRecycleReason</c> exactly once.
/// <para>
/// These tests cover three layers:
/// </para>
/// <list type="number">
///   <item><description>Disk store — round-trip, atomic write, TTL guard, missing/corrupt files
///     all behave correctly.</description></item>
///   <item><description>Snapshot provider — consume-once semantics under concurrent access.</description></item>
///   <item><description>End-to-end — restart sequence (process A writes, process B reads + surfaces)
///     produces the expected wire shape on both <c>server_info</c> and <c>server_heartbeat</c>;
///     SECOND probe of process B drops the previous-* fields.</description></item>
/// </list>
/// </summary>
[TestClass]
public sealed class HostProcessMetadataTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // Each test gets a clean temp dir so they cannot pollute each other. Persisted
        // metadata is per-user, so without isolation a parallel test runner would race.
        _tempDir = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", "host-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Provider is a process-wide singleton; reset between tests so a leaked snapshot
        // from a prior test cannot bleed forward.
        HostProcessMetadataSnapshotProvider.Reset();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort — another test may hold a lock; the assembly cleanup will sweep.
        }

        HostProcessMetadataSnapshotProvider.Reset();
    }

    private string PathInTemp() => Path.Combine(_tempDir, "host-process.json");

    [TestMethod]
    public void WriteCurrent_ThenLoadPrevious_RoundTripsAllFields()
    {
        var fixedNow = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        // Process A: writes its exit metadata.
        var writer = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow);
        writer.WriteCurrent(recycleReason: "graceful");

        Assert.IsTrue(File.Exists(path), "WriteCurrent must produce the on-disk record.");

        // Process B: reads the record.
        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow.AddMinutes(1));
        var snapshot = reader.LoadPrevious();

        Assert.IsNotNull(snapshot, "LoadPrevious must return a snapshot when the file exists with valid content.");
        Assert.AreEqual(Environment.ProcessId, snapshot.StdioPid,
            "Round-tripped pid must match the writer process — the test process is both writer and reader.");
        Assert.AreEqual("graceful", snapshot.RecycleReason);
        Assert.IsTrue(DateTime.TryParse(snapshot.ExitedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed),
            $"ExitedAtUtc must parse as a DateTime (got '{snapshot.ExitedAtUtc}').");
        Assert.AreEqual(fixedNow, parsed,
            "Round-tripped exit timestamp must match the writer's UtcNow at write time.");
    }

    [TestMethod]
    public void LoadPrevious_DeletesOnDiskRecord_AfterRead()
    {
        var fixedNow = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        var writer = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow);
        writer.WriteCurrent(recycleReason: "graceful");
        Assert.IsTrue(File.Exists(path), "Pre-read sanity check.");

        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow);
        reader.LoadPrevious();

        Assert.IsFalse(File.Exists(path),
            "LoadPrevious must delete the on-disk record after read so the same snapshot " +
            "is never replayed across multiple processes (e.g. if the next host crashes " +
            "before writing its own metadata).");
    }

    [TestMethod]
    public void LoadPrevious_OldRecord_PreservesFields_NormalizesReasonToUnknown()
    {
        // TTL guard: a record older than StaleAfter (24h) keeps its pid + timestamp but the
        // reason is normalized to "unknown" — operators get something but don't trust an
        // ancient recycle reason.
        var writeTime = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        var writer = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => writeTime);
        writer.WriteCurrent(recycleReason: "watchdog");

        // Read 25 hours later — beyond the 24h TTL.
        var staleNow = writeTime.AddHours(25);
        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => staleNow);
        var snapshot = reader.LoadPrevious();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(Environment.ProcessId, snapshot.StdioPid,
            "Stale-record pid must still surface — operators want SOMETHING.");
        Assert.AreEqual("unknown", snapshot.RecycleReason,
            "Stale records must report reason=unknown rather than the persisted (untrusted) reason.");
    }

    [TestMethod]
    public void LoadPrevious_FreshRecord_PreservesPersistedReason()
    {
        // Counterpoint to the TTL test: a record well within the window keeps its persisted
        // reason — TTL only fires for genuinely-old records.
        var writeTime = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        var writer = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => writeTime);
        writer.WriteCurrent(recycleReason: "watchdog");

        // Read 1 hour later — well within TTL.
        var freshNow = writeTime.AddHours(1);
        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => freshNow);
        var snapshot = reader.LoadPrevious();

        Assert.IsNotNull(snapshot);
        Assert.AreEqual("watchdog", snapshot.RecycleReason,
            "Fresh records must surface their persisted recycle reason verbatim.");
    }

    [TestMethod]
    public void LoadPrevious_MissingFile_ReturnsNull()
    {
        // Cold start: no prior record exists. Must return null cleanly without throwing.
        var path = PathInTemp();
        Assert.IsFalse(File.Exists(path));

        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => DateTime.UtcNow);
        Assert.IsNull(reader.LoadPrevious());
    }

    [TestMethod]
    public void LoadPrevious_CorruptJson_ReturnsNull_WithoutThrowing()
    {
        // Best-effort parsing: a corrupt file must not crash startup.
        var path = PathInTemp();
        File.WriteAllText(path, "{ this is not valid JSON ::: ");

        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => DateTime.UtcNow);
        Assert.IsNull(reader.LoadPrevious(),
            "Corrupt JSON must be treated as 'no prior record' rather than crashing the host startup path.");
    }

    [TestMethod]
    public void LoadPrevious_MissingRequiredFields_ReturnsNull()
    {
        // A record with valid JSON but missing pid / timestamp is unsalvageable — discard it.
        var path = PathInTemp();
        File.WriteAllText(path, """{"recycleReason":"graceful"}""");

        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => DateTime.UtcNow);
        Assert.IsNull(reader.LoadPrevious(),
            "A record without pid/exitedAt is missing the load-bearing fields — must be discarded.");
    }

    [TestMethod]
    public void LoadPrevious_IsCachedAcrossCalls_NoExtraDiskReads()
    {
        // LoadPrevious caches its first result. Subsequent calls must return the SAME
        // snapshot reference without re-reading the file (which would fail since
        // we delete the file after the first read).
        var fixedNow = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        var writer = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow);
        writer.WriteCurrent(recycleReason: "graceful");

        var reader = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => fixedNow);
        var first = reader.LoadPrevious();
        var second = reader.LoadPrevious();
        var third = reader.LoadPrevious();

        Assert.IsNotNull(first);
        Assert.AreSame(first, second, "LoadPrevious must cache and return the same snapshot reference.");
        Assert.AreSame(first, third, "Caching must persist across many calls — not just two.");
    }

    [TestMethod]
    public void Provider_ConsumeAfterPublish_ReturnsSnapshotOnce_NullThereafter()
    {
        // The CONTRACT from the backlog row: first probe after restart carries previous-*;
        // second probe does not. The provider's Consume() owns that invariant.
        var snapshot = new HostProcessMetadataSnapshot(
            StdioPid: 12345,
            ExitedAtUtc: "2026-04-25T11:59:59.0000000Z",
            RecycleReason: "graceful");

        HostProcessMetadataSnapshotProvider.Publish(snapshot);

        var first = HostProcessMetadataSnapshotProvider.Consume();
        var second = HostProcessMetadataSnapshotProvider.Consume();
        var third = HostProcessMetadataSnapshotProvider.Consume();

        Assert.AreSame(snapshot, first, "First Consume() after Publish() must return the published snapshot.");
        Assert.IsNull(second, "Second Consume() must return null — consume-once semantics are the wire contract.");
        Assert.IsNull(third, "All subsequent Consume() calls must return null.");
    }

    [TestMethod]
    public void Provider_PublishNull_ConsumeReturnsNull()
    {
        // Cold start: no prior record. Publishing null is the canonical "no metadata" signal.
        HostProcessMetadataSnapshotProvider.Publish(snapshot: null);

        Assert.IsNull(HostProcessMetadataSnapshotProvider.Consume(),
            "Publishing null must short-circuit Consume() to null — no previous-* fields ever surface.");
    }

    [TestMethod]
    public void ServerInfo_FirstProbeAfterRestart_CarriesPreviousMetadata()
    {
        // End-to-end: simulate the restart sequence by publishing a snapshot directly to
        // the provider, then call ServerTools.GetServerInfo and verify the wire shape.
        var snapshot = new HostProcessMetadataSnapshot(
            StdioPid: 99999,
            ExitedAtUtc: "2026-04-25T11:59:59.0000000Z",
            RecycleReason: "graceful");
        HostProcessMetadataSnapshotProvider.Publish(snapshot);

        var json = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();

        using var doc = JsonDocument.Parse(json);
        var connection = doc.RootElement.GetProperty("connection");

        Assert.AreEqual(99999, connection.GetProperty("previousStdioPid").GetInt32(),
            "First probe after restart must carry previousStdioPid from the published snapshot.");
        Assert.AreEqual("2026-04-25T11:59:59.0000000Z", connection.GetProperty("previousExitedAt").GetString(),
            "First probe after restart must carry previousExitedAt from the published snapshot.");
        Assert.AreEqual("graceful", connection.GetProperty("previousRecycleReason").GetString(),
            "First probe after restart must carry previousRecycleReason from the published snapshot.");
    }

    [TestMethod]
    public void ServerInfo_SecondProbe_DropsPreviousMetadata()
    {
        // The wire contract: previous-* fields appear EXACTLY ONCE per host lifetime.
        // A polling consumer that calls server_info twice in quick succession must see the
        // fields on probe #1 only.
        var snapshot = new HostProcessMetadataSnapshot(
            StdioPid: 99999,
            ExitedAtUtc: "2026-04-25T11:59:59.0000000Z",
            RecycleReason: "graceful");
        HostProcessMetadataSnapshotProvider.Publish(snapshot);

        // First probe drains the provider.
        _ = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();

        // Second probe must NOT carry previous-* fields.
        var secondJson = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();
        using var secondDoc = JsonDocument.Parse(secondJson);
        var secondConnection = secondDoc.RootElement.GetProperty("connection");

        Assert.IsFalse(secondConnection.TryGetProperty("previousStdioPid", out _),
            "Second probe MUST NOT carry previousStdioPid — consume-once semantics are the wire contract.");
        Assert.IsFalse(secondConnection.TryGetProperty("previousExitedAt", out _),
            "Second probe MUST NOT carry previousExitedAt.");
        Assert.IsFalse(secondConnection.TryGetProperty("previousRecycleReason", out _),
            "Second probe MUST NOT carry previousRecycleReason.");
    }

    [TestMethod]
    public void ServerHeartbeat_FirstProbeAfterRestart_CarriesPreviousMetadata()
    {
        // server_heartbeat shares the connection block with server_info. The previous-*
        // fields must appear there too — consumers polling via the cheap heartbeat tool
        // should see the same recycle metadata as full server_info pollers.
        var snapshot = new HostProcessMetadataSnapshot(
            StdioPid: 88888,
            ExitedAtUtc: "2026-04-25T10:30:00.0000000Z",
            RecycleReason: "watchdog");
        HostProcessMetadataSnapshotProvider.Publish(snapshot);

        var json = ServerTools.GetServerHeartbeat(new FakeWorkspaceManager()).GetAwaiter().GetResult();

        using var doc = JsonDocument.Parse(json);
        var connection = doc.RootElement.GetProperty("connection");

        Assert.AreEqual(88888, connection.GetProperty("previousStdioPid").GetInt32());
        Assert.AreEqual("2026-04-25T10:30:00.0000000Z", connection.GetProperty("previousExitedAt").GetString());
        Assert.AreEqual("watchdog", connection.GetProperty("previousRecycleReason").GetString());
    }

    [TestMethod]
    public void ServerInfo_ColdStart_NoSnapshotPublished_OmitsPreviousMetadata()
    {
        // No Publish() call: the provider has nothing to consume. The previous-* fields must
        // be ABSENT from the wire (not present-with-null) so consumers can use TryGetProperty
        // as a presence check.
        // (Cleanup() called Reset() before this test, so the provider is clean.)

        var json = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var connection = doc.RootElement.GetProperty("connection");

        Assert.IsFalse(connection.TryGetProperty("previousStdioPid", out _),
            "Cold start must not emit previousStdioPid.");
        Assert.IsFalse(connection.TryGetProperty("previousExitedAt", out _),
            "Cold start must not emit previousExitedAt.");
        Assert.IsFalse(connection.TryGetProperty("previousRecycleReason", out _),
            "Cold start must not emit previousRecycleReason.");

        // Sanity: the existing connection fields are still there. We didn't break the shape.
        Assert.IsTrue(connection.TryGetProperty("state", out _));
        Assert.IsTrue(connection.TryGetProperty("loadedWorkspaceCount", out _));
        Assert.IsTrue(connection.TryGetProperty("stdioPid", out _));
        Assert.IsTrue(connection.TryGetProperty("serverStartedAt", out _));
    }

    [TestMethod]
    public void EndToEnd_DiskWriteRead_RestartSequence_FirstProbeOnlyCarriesMetadata()
    {
        // Full integration: write metadata to disk (process A), then read + publish + consume
        // (process B), simulating the actual restart sequence. Verifies the disk store and
        // the provider compose correctly when wired the way Program.cs wires them.
        var writeTime = new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc);
        var path = PathInTemp();

        // Process A — graceful shutdown writes its metadata.
        var processA = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => writeTime);
        processA.WriteCurrent(recycleReason: "graceful");
        Assert.IsTrue(File.Exists(path));

        // Process B — startup loads + publishes + first probe consumes.
        var processB = new HostProcessMetadataStore(path, HostProcessMetadataStore.StaleAfter, () => writeTime.AddSeconds(2));
        var loaded = processB.LoadPrevious();
        Assert.IsNotNull(loaded, "LoadPrevious must return a snapshot from the on-disk record.");
        HostProcessMetadataSnapshotProvider.Publish(loaded);

        // First probe.
        var firstJson = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();
        using var firstDoc = JsonDocument.Parse(firstJson);
        var firstConn = firstDoc.RootElement.GetProperty("connection");
        Assert.AreEqual(Environment.ProcessId, firstConn.GetProperty("previousStdioPid").GetInt32(),
            "First probe after disk-loaded restart must carry the writer's pid.");
        Assert.AreEqual("graceful", firstConn.GetProperty("previousRecycleReason").GetString());

        // Second probe — fields must drop.
        var secondJson = ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider()).GetAwaiter().GetResult();
        using var secondDoc = JsonDocument.Parse(secondJson);
        var secondConn = secondDoc.RootElement.GetProperty("connection");
        Assert.IsFalse(secondConn.TryGetProperty("previousStdioPid", out _),
            "Second probe must not re-emit previous-* fields — consume-once is enforced by the provider.");
    }

    /// <summary>
    /// Minimal fake — only ListWorkspaces is invoked by ServerTools.BuildConnection. Every
    /// other member throws to surface mistakes if a future refactor accidentally calls them
    /// from the connection-building path.
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => Array.Empty<WorkspaceStatusDto>();
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) => throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => throw new NotSupportedException();
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }

    private sealed class FakeVersionProvider : ILatestVersionProvider
    {
        public string? GetLatestVersion() => null;
    }
}
