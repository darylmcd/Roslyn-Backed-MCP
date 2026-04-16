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
    private sealed class FakeVersionProvider(string? latest) : ILatestVersionProvider
    {
        public string? GetLatestVersion() => latest;
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

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();
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
        using var doc = JsonDocument.Parse(json);

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual(JsonValueKind.Object, update.ValueKind, "update block must be present when registry returned a value");

        Assert.IsTrue(update.TryGetProperty("updateAvailable", out var updateAvail));
        Assert.IsFalse(updateAvail.GetBoolean(), "updateAvailable must be false when registry version <= current");

        Assert.IsTrue(update.TryGetProperty("latest", out var latest));
        Assert.AreEqual(JsonValueKind.Null, latest.ValueKind,
            "latest must be null when registry version is not strictly greater than current — pre-fix it surfaced the older value");
    }

    [TestMethod]
    public async Task ServerInfo_RegistryReportsNewerVersion_LatestPopulatedAndUpdateAvailable()
    {
        // 999.0.0 will always be > current.
        var newer = "999.0.0";
        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider(newer));
        using var doc = JsonDocument.Parse(json);

        var update = doc.RootElement.GetProperty("update");
        Assert.IsTrue(update.GetProperty("updateAvailable").GetBoolean());
        Assert.AreEqual(newer, update.GetProperty("latest").GetString(),
            "latest must surface when registry version is strictly greater than current");
    }

    [TestMethod]
    public async Task ServerInfo_RegistryReturnedNull_UpdateBlockIsNull()
    {
        // When the version checker hasn't completed yet, the entire update block stays
        // null — no false signal of a missing update.
        var json = await ServerTools.GetServerInfo(new FakeWorkspaceManager(), new FakeVersionProvider(null));
        using var doc = JsonDocument.Parse(json);

        var update = doc.RootElement.GetProperty("update");
        Assert.AreEqual(JsonValueKind.Null, update.ValueKind);
    }
}
