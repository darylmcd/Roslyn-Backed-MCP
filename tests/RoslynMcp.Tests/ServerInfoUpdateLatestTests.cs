using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `server-info-update-latest-inverted` (P4): Jellyfin 2026-04-16 §1
/// reproduced `server_info` reporting `latest=1.16.0` while `current=1.18.2`. The
/// update.latest field surfaced any cached registry value regardless of comparison
/// to current. Post-fix: `latest` is only populated when strictly greater than current.
/// </summary>
[TestClass]
public sealed class ServerInfoUpdateLatestTests
{
    private sealed class FakeVersionProvider(
        string? latest,
        VersionCheckStatus status = VersionCheckStatus.Succeeded,
        DateTime? lastCheckedAt = null) : ILatestVersionProvider
    {
        public string? GetLatestVersion() => latest;
        public VersionCheckStatus LastCheckStatus { get; } = status;
        public DateTime? LastCheckedAt { get; } = lastCheckedAt;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("simulated NuGet failure");
    }

    private sealed class SuccessThenThrowingHandler : HttpMessageHandler
    {
        private int _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"versions":["1.0.0"]}"""),
                });
            }

            throw new HttpRequestException("simulated NuGet refresh failure");
        }
    }

    /// <summary>
    /// Minimal IWorkspaceManager fake matching the shape SurfaceCatalogTests uses.
    /// All members throw NotSupportedException except those that GetServerInfo touches
    /// (ListWorkspaces).
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
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

    [TestMethod]
    public async Task ServerInfo_RegistryReportsOlderVersion_LatestIsNull_UpdateAvailableFalse()
    {
        // Compute the running version so the test compares against the real binary.
        var runningVersion = typeof(ServerTools).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? typeof(ServerTools).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Pretend the registry returned an OLDER version than current (the bug repro shape).
        var older = "0.0.1";
        Assert.IsTrue(
            Version.TryParse(runningVersion, out var runningParsed) &&
            Version.TryParse(older, out var olderParsed) &&
            olderParsed < runningParsed,
            $"test fixture broken: '{older}' must be < running '{runningVersion}'");

        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider(older));
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual(JsonValueKind.Object, update.ValueKind, "update block must be present when registry returned a value");

        Assert.IsTrue(update.TryGetProperty("updateAvailable", out var updateAvail));
        Assert.IsFalse(updateAvail.GetBoolean(), "updateAvailable must be false when registry version <= current");

        Assert.IsTrue(update.TryGetProperty("latest", out var latest));
        Assert.AreEqual(JsonValueKind.Null, latest.ValueKind,
            "latest must be null when registry version is not strictly greater than current — pre-fix it surfaced the older value");

        Assert.AreEqual("succeeded", update.GetProperty("checkStatus").GetString(),
            "A successful check with no newer package must be distinguishable from pending/failed/timed-out checks.");
    }

    [TestMethod]
    public async Task ServerInfo_RegistryReportsNewerVersion_LatestPopulatedAndUpdateAvailable()
    {
        // 999.0.0 will always be > current.
        var newer = "999.0.0";
        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider(newer));
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.IsTrue(update.GetProperty("updateAvailable").GetBoolean());
        Assert.AreEqual(newer, update.GetProperty("latest").GetString(),
            "latest must surface when registry version is strictly greater than current");
        Assert.AreEqual("succeeded", update.GetProperty("checkStatus").GetString());
    }

    [TestMethod]
    public async Task ServerInfo_CheckPending_UpdateBlockReportsPending()
    {
        // When the version checker hasn't completed yet, update.latest stays null but the
        // operator-visible status now explains that the check is still in flight.
        var json = await ServerTools.GetServerInfo(
            new FakeWorkspaceManager(),
            new FakeVersionProvider(null, VersionCheckStatus.Pending));
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual(JsonValueKind.Object, update.ValueKind);
        Assert.AreEqual("pending", update.GetProperty("checkStatus").GetString());
        Assert.AreEqual(JsonValueKind.Null, update.GetProperty("latest").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, update.GetProperty("lastCheckedAt").ValueKind);
    }

    [TestMethod]
    public async Task ServerInfo_CheckFailed_UpdateBlockReportsFailureAndCompletionTime()
    {
        var completedAt = new DateTime(2026, 6, 8, 14, 0, 0, DateTimeKind.Utc);
        var json = await ServerTools.GetServerInfo(
            new FakeWorkspaceManager(),
            new FakeVersionProvider(null, VersionCheckStatus.Failed, completedAt));
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual("failed", update.GetProperty("checkStatus").GetString());
        Assert.AreEqual(completedAt.ToString("O"), update.GetProperty("lastCheckedAt").GetString());
        Assert.AreEqual(JsonValueKind.Null, update.GetProperty("latest").ValueKind);
        Assert.IsFalse(update.GetProperty("updateAvailable").GetBoolean());
    }

    [TestMethod]
    public async Task ServerInfo_RealFailedChecker_DoesNotResetStatusToPending()
    {
        using var handler = new ThrowingHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call starts the background check.");
        await WaitForTerminalStatusAsync(checker);
        Assert.AreEqual(VersionCheckStatus.Failed, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt);

        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), checker);
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual("failed", update.GetProperty("checkStatus").GetString(),
            "server_info must report the completed failure instead of starting a new pending check and hiding it.");
        Assert.AreNotEqual(JsonValueKind.Null, update.GetProperty("lastCheckedAt").ValueKind);
    }

    [TestMethod]
    public async Task ServerInfo_RealCheckerFailedRefreshAfterSuccess_DoesNotResetStatusToPending()
    {
        using var handler = new SuccessThenThrowingHandler();
        var checker = new NuGetVersionChecker(new TestHttpClientFactory(handler));

        Assert.IsNull(checker.GetLatestVersion(), "First call starts the successful background check.");
        await WaitForTerminalStatusAsync(checker);
        Assert.AreEqual(VersionCheckStatus.Succeeded, checker.LastCheckStatus);
        Assert.AreEqual("1.0.0", checker.GetLatestVersion());

        ForceCacheExpired(checker);
        Assert.AreEqual("1.0.0", checker.GetLatestVersion(),
            "Expired cache refresh should return the stale value while the refresh runs.");
        await WaitForTerminalStatusAsync(checker);
        Assert.AreEqual(VersionCheckStatus.Failed, checker.LastCheckStatus);
        Assert.IsNotNull(checker.LastCheckedAt);

        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), checker);
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual("failed", update.GetProperty("checkStatus").GetString(),
            "server_info must report a failed refresh instead of starting another pending check.");
        Assert.AreNotEqual(JsonValueKind.Null, update.GetProperty("lastCheckedAt").ValueKind);
    }

    [TestMethod]
    public async Task ServerInfo_CheckTimedOut_UpdateBlockReportsTimeoutAndCompletionTime()
    {
        var completedAt = new DateTime(2026, 6, 8, 14, 5, 0, DateTimeKind.Utc);
        var json = await ServerTools.GetServerInfo(
            new FakeWorkspaceManager(),
            new FakeVersionProvider(null, VersionCheckStatus.TimedOut, completedAt));
        using var doc = JsonDocument.Parse(json.TextPayload());

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual("timedOut", update.GetProperty("checkStatus").GetString());
        Assert.AreEqual(completedAt.ToString("O"), update.GetProperty("lastCheckedAt").GetString());
        Assert.AreEqual(JsonValueKind.Null, update.GetProperty("latest").ValueKind);
        Assert.IsFalse(update.GetProperty("updateAvailable").GetBoolean());
    }

    private static async Task WaitForTerminalStatusAsync(NuGetVersionChecker checker)
    {
        // Await the in-flight background fetch directly via the internal test seam rather than
        // spin-looping on the observable status — deterministic and free of timing sensitivity
        // under heavy CI parallel load. The bounded WaitAsync guards against a hung handler.
        var pending = checker.PendingCheckForTest;
        Assert.IsNotNull(pending, "Expected a background version check to be in flight.");
        await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.IsTrue(
            checker.LastCheckStatus is VersionCheckStatus.Failed or VersionCheckStatus.TimedOut or VersionCheckStatus.Succeeded,
            "Background version check should have reached a terminal status.");
    }

    private static void ForceCacheExpired(NuGetVersionChecker checker)
    {
        var field = typeof(NuGetVersionChecker).GetField(
            "_checkedAtUtc",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Test expects NuGetVersionChecker to keep the cache timestamp in _checkedAtUtc.");
        field.SetValue(checker, DateTime.UtcNow - TimeSpan.FromHours(2));
    }
}
