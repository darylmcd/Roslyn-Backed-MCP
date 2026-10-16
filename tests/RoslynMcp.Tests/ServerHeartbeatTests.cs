using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>mcp-connection-session-resilience</c> (P3-correctness):
/// consumer repos reported `Not connected` and fell back to Grep / Bash because
/// `server_info` exposed no explicit connection-readiness signal. Post-fix the
/// server surfaces a `connection` subfield (state / loadedWorkspaceCount /
/// stdioPid / serverStartedAt) on `server_info` AND ships a lightweight
/// `server_heartbeat` tool that returns the same shape without the full
/// version + catalog payload.
/// </summary>
[TestClass]
public sealed class ServerHeartbeatTests
{
    private static WorkspaceStatusDto LoadedWorkspaceStatus() => new(
        WorkspaceId: "ws-0",
        LoadedPath: "C:/fake/sln-0.sln",
        WorkspaceVersion: 1,
        SnapshotToken: "ws-0:1",
        LoadedAtUtc: DateTimeOffset.UtcNow,
        ProjectCount: 1,
        DocumentCount: 1,
        Projects: Array.Empty<ProjectStatusDto>(),
        IsLoaded: true,
        IsStale: false,
        WorkspaceDiagnostics: Array.Empty<DiagnosticDto>());

    [TestMethod]
    public async Task Heartbeat_NoWorkspaceLoaded_ReturnsIdleWithZeroCount()
    {
        // connection-state-ready-unsatisfiable-preload: pre-load state is "idle", a
        // terminal label, not the previous "initializing" which incorrectly implied a
        // transient step that would auto-advance. The server only transitions via an
        // explicit workspace_load call.
        var json = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(), new ServerProcessMetadata());
        using var doc = JsonDocument.Parse(json.TextPayload());

        var connection = doc.RootElement.GetProperty("connection");
        Assert.AreEqual("idle", connection.GetProperty("state").GetString(),
            "state must be 'idle' when no workspace has been loaded yet (terminal pre-load state, NOT 'initializing').");
        Assert.AreEqual(0, connection.GetProperty("loadedWorkspaceCount").GetInt32());
        AssertIdentityFieldsPresent(connection);
    }

    [TestMethod]
    public async Task Heartbeat_OneWorkspaceLoaded_ReturnsReadyWithCountOne()
    {
        var json = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(LoadedWorkspaceStatus()), new ServerProcessMetadata());
        using var doc = JsonDocument.Parse(json.TextPayload());

        var connection = doc.RootElement.GetProperty("connection");
        Assert.AreEqual("ready", connection.GetProperty("state").GetString(),
            "state must be 'ready' once at least one workspace is loaded.");
        Assert.AreEqual(1, connection.GetProperty("loadedWorkspaceCount").GetInt32());
        AssertIdentityFieldsPresent(connection);
    }

    [TestMethod]
    public async Task Heartbeat_PreLoadToPostLoadTransition_IdleToReady()
    {
        // connection-state-ready-unsatisfiable-preload: explicit test of the only
        // allowed state transition. The server starts at `idle` (pre-load) and flips
        // to `ready` the moment ListWorkspaces() reports a loaded session. There is
        // no transient intermediate `initializing` step — the label `idle` is terminal
        // until workspace_load is called. A hard-gate prompt that polls through this
        // transition should observe exactly two distinct states in order: idle, ready.
        var processMetadata = new ServerProcessMetadata();
        var preLoadJson = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(), processMetadata);
        using var preLoadDoc = JsonDocument.Parse(preLoadJson.TextPayload());
        Assert.AreEqual(
            "idle",
            preLoadDoc.RootElement.GetProperty("connection").GetProperty("state").GetString(),
            "pre-load state must be 'idle' — reverts to the broken 'initializing' label if this fails.");

        var postLoadJson = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(LoadedWorkspaceStatus()), processMetadata);
        using var postLoadDoc = JsonDocument.Parse(postLoadJson.TextPayload());
        Assert.AreEqual(
            "ready",
            postLoadDoc.RootElement.GetProperty("connection").GetProperty("state").GetString(),
            "post-load state must flip to 'ready' once at least one workspace is loaded.");
    }

    [TestMethod]
    public async Task ServerInfo_PreLoad_ReportsIdleNotInitializing()
    {
        // connection-state-ready-unsatisfiable-preload: matches the heartbeat's shape
        // but verifies the inline `connection` block on `server_info`. Prompts like
        // `deep-review-and-refactor.md`'s Phase -1 hard gate previously saw
        // `state=initializing` here and waited for it to flip without ever calling
        // workspace_load — that broken polling loop is what this fix closes.
        var json = await ServerTools.GetServerInfo(new FailClosedWorkspaceManagerStub(), new FixedLatestVersionProvider(null, VersionCheckStatus.Pending, null), new ServerProcessMetadata());
        using var doc = JsonDocument.Parse(json.TextPayload());

        var connection = doc.RootElement.GetProperty("connection");
        Assert.AreEqual("idle", connection.GetProperty("state").GetString(),
            "server_info.connection.state must be 'idle' pre-load, not 'initializing'.");
        Assert.AreNotEqual("initializing", connection.GetProperty("state").GetString(),
            "server_info.connection.state must NOT revert to the legacy 'initializing' label.");
    }

    [TestMethod]
    public async Task Heartbeat_PayloadIsLighterThanServerInfo()
    {
        // Positive shape check: heartbeat intentionally omits version / catalog / update.
        // If a future refactor accidentally copies the entire server_info payload into
        // the heartbeat, this test catches it.
        var heartbeatJson = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(LoadedWorkspaceStatus()), new ServerProcessMetadata());
        using var heartbeatDoc = JsonDocument.Parse(heartbeatJson.TextPayload());

        var root = heartbeatDoc.RootElement;
        Assert.IsTrue(root.TryGetProperty("connection", out _), "heartbeat must include connection");
        Assert.IsFalse(root.TryGetProperty("version", out _), "heartbeat must NOT carry the version payload");
        Assert.IsFalse(root.TryGetProperty("surface", out _), "heartbeat must NOT carry the catalog summary");
        Assert.IsFalse(root.TryGetProperty("update", out _), "heartbeat must NOT carry the update metadata");
        Assert.IsFalse(root.TryGetProperty("capabilities", out _), "heartbeat must NOT carry the capabilities block");
    }

    [TestMethod]
    public async Task ServerInfo_CarriesConnectionSubfield_WithSameShapeAsHeartbeat()
    {
        // The `connection` block on server_info must match the heartbeat's shape so
        // consumers can use whichever poll they prefer without shape surprises.
        var processMetadata = new ServerProcessMetadata();
        var infoJson = await ServerTools.GetServerInfo(new FailClosedWorkspaceManagerStub(LoadedWorkspaceStatus()), new FixedLatestVersionProvider(null, VersionCheckStatus.Pending, null), processMetadata);
        using var infoDoc = JsonDocument.Parse(infoJson.TextPayload());
        var infoConn = infoDoc.RootElement.GetProperty("connection");

        Assert.AreEqual("ready", infoConn.GetProperty("state").GetString());
        Assert.AreEqual(1, infoConn.GetProperty("loadedWorkspaceCount").GetInt32());
        AssertIdentityFieldsPresent(infoConn);

        var heartbeatJson = await ServerTools.GetServerHeartbeat(new FailClosedWorkspaceManagerStub(LoadedWorkspaceStatus()), processMetadata);
        using var heartbeatDoc = JsonDocument.Parse(heartbeatJson.TextPayload());
        var heartbeatConn = heartbeatDoc.RootElement.GetProperty("connection");

        // Match every property name from the heartbeat's connection block on server_info.
        foreach (var prop in heartbeatConn.EnumerateObject())
        {
            Assert.IsTrue(infoConn.TryGetProperty(prop.Name, out _),
                $"server_info.connection is missing property '{prop.Name}' that heartbeat exposes — shapes must stay aligned.");
        }
    }

    /// <summary>
    /// Identity invariants for the connection block: <c>stdioPid</c> is the current
    /// process id, <c>serverStartedAt</c> is a parseable ISO-8601 UTC timestamp that is
    /// not in the future. Both come from the host, not from the workspace manager.
    /// </summary>
    private static void AssertIdentityFieldsPresent(JsonElement connection)
    {
        Assert.IsTrue(connection.TryGetProperty("stdioPid", out var pidElement), "connection.stdioPid must be present");
        Assert.AreEqual(Environment.ProcessId, pidElement.GetInt32(),
            "connection.stdioPid must match Environment.ProcessId of the running test host.");

        Assert.IsTrue(connection.TryGetProperty("serverStartedAt", out var startedElement), "connection.serverStartedAt must be present");
        var rawStartedAt = startedElement.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(rawStartedAt), "connection.serverStartedAt must be a non-empty string.");
        Assert.IsTrue(DateTime.TryParse(rawStartedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var startedAt),
            $"connection.serverStartedAt must be parseable as a DateTime (got '{rawStartedAt}').");
        Assert.IsTrue(startedAt <= DateTime.UtcNow.AddSeconds(5),
            "connection.serverStartedAt must not be in the future (small skew tolerated).");
    }
}
