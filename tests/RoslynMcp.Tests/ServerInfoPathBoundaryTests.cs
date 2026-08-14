using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>sanctioned-roots-empty-boundary-fails-silently-until-first-call</c>.
/// <para>
/// An unconfigured filesystem boundary is fail-closed, so every path-taking tool rejects its
/// input — but nothing surfaced that state until the first call threw. A NuGet / hand-rolled
/// install (the plugin and Desktop manifests ship <c>ROSLYNMCP_SANCTIONED_ROOTS</c>, the NuGet
/// package does not) therefore looked healthy right up until first use. <c>server_info</c> now
/// reports the boundary so the misconfiguration is diagnosable up front.
/// </para>
/// </summary>
[TestClass]
public sealed class ServerInfoPathBoundaryTests
{
    /// <summary>
    /// The load-bearing security property: the boundary is a server-owned control, so the wire
    /// shape carries a COUNT and never the configured paths. A client that could read the roots
    /// would learn the host's filesystem layout from a read-only diagnostic tool.
    /// </summary>
    [TestMethod]
    public void BuildPathBoundary_NeverExposesConfiguredRootPaths()
    {
        var options = new SecurityOptions
        {
            SanctionedRoots = ["C:/secret-project", "D:/another-secret"],
        };

        var boundary = ServerTools.BuildPathBoundary(options);

        Assert.IsNotNull(boundary);
        Assert.AreEqual(2, boundary.ConfiguredRootCount);
        var serialized = System.Text.Json.JsonSerializer.Serialize(boundary);
        StringAssert.DoesNotMatch(
            serialized,
            new System.Text.RegularExpressions.Regex("secret", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "server_info must report the sanctioned-root COUNT, never the paths themselves.");
    }

    /// <summary>
    /// Null snapshot means the host was never booted (unit-test path), which is "unknown" — NOT
    /// "unconfigured". Fabricating a fail-closed boundary here would report a misconfiguration
    /// that does not exist, which is the same silent-wrong-answer class this row removes.
    /// </summary>
    [TestMethod]
    public void BuildPathBoundary_UnbootedHost_ReturnsNullRatherThanFabricatingUnconfigured()
    {
        Assert.IsNull(ServerTools.BuildPathBoundary(null));
    }

    [TestMethod]
    public void BuildPathBoundary_RootsConfigured_IsEnforcingWithNoHint()
    {
        var boundary = ServerTools.BuildPathBoundary(new SecurityOptions
        {
            SanctionedRoots = ["."],
        });

        Assert.IsNotNull(boundary);
        Assert.IsTrue(boundary.Enforcing);
        Assert.IsFalse(boundary.FailOpen);
        Assert.AreEqual(1, boundary.ConfiguredRootCount);
        Assert.IsNull(boundary.Hint, "A correctly configured boundary must not emit a remediation hint.");
    }

    /// <summary>
    /// The shape that previously looked healthy until first use. The hint must name BOTH the
    /// variable to set and the escape hatch — an operator hitting this mid-session needs the
    /// remediation without leaving the tool output.
    /// </summary>
    [TestMethod]
    public void BuildPathBoundary_ZeroRootsFailClosed_HintsBothVariables()
    {
        var boundary = ServerTools.BuildPathBoundary(new SecurityOptions());

        Assert.IsNotNull(boundary);
        Assert.IsFalse(boundary.Enforcing);
        Assert.IsFalse(boundary.FailOpen);
        Assert.AreEqual(0, boundary.ConfiguredRootCount);
        Assert.IsNotNull(boundary.Hint);
        StringAssert.Contains(boundary.Hint, "ROSLYNMCP_SANCTIONED_ROOTS");
        StringAssert.Contains(boundary.Hint, "ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN");
        StringAssert.Contains(
            boundary.Hint,
            "discovery",
            "The hint must mention the silent half — query-anchored discovery returns nothing " +
            "rather than throwing when the boundary is empty.");
    }

    /// <summary>
    /// Fail-open is a deliberate operator choice, so it is not an error — but it leaves the host
    /// unbounded, which is worth saying out loud in a diagnostic.
    /// </summary>
    [TestMethod]
    public void BuildPathBoundary_ZeroRootsFailOpen_ReportsUnboundedNotEnforcing()
    {
        var boundary = ServerTools.BuildPathBoundary(new SecurityOptions
        {
            PathValidationFailOpen = true,
        });

        Assert.IsNotNull(boundary);
        Assert.IsFalse(boundary.Enforcing);
        Assert.IsTrue(boundary.FailOpen);
        Assert.IsNotNull(boundary.Hint, "An unbounded host should still say so.");
        StringAssert.Contains(boundary.Hint, "unbounded");
    }

    /// <summary>
    /// A non-empty boundary is always enforced; <c>PathValidationFailOpen</c> only ever rescues
    /// the zero-root case (ADR 0002 decision 2). Guards against a future edit letting the escape
    /// hatch punch through a configured boundary.
    /// </summary>
    [TestMethod]
    public void BuildPathBoundary_FailOpenDoesNotDisableAConfiguredBoundary()
    {
        var boundary = ServerTools.BuildPathBoundary(new SecurityOptions
        {
            SanctionedRoots = ["."],
            PathValidationFailOpen = true,
        });

        Assert.IsNotNull(boundary);
        Assert.IsTrue(
            boundary.Enforcing,
            "Fail-open must never report a configured boundary as unenforced.");
        Assert.IsNull(boundary.Hint);
    }

    /// <summary>
    /// Wiring test. The helper above is pure, so every assertion in this class still passes if
    /// <c>server_info</c> stops calling it — deleting the <c>PathBoundary:</c> argument would be
    /// invisible. This drives the real tool and asserts the field reaches the wire under its
    /// camelCase name, so the projection cannot be silently disconnected from the response.
    /// </summary>
    [TestMethod]
    public async Task ServerInfo_CarriesPathBoundary_FromTheStartupSnapshot()
    {
        var previous = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = new SecurityOptions { SanctionedRoots = ["."] };

            var json = await ServerTools.GetServerInfo(
                new FakeWorkspaceManager(),
                new FakeVersionProvider(null));

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(
                doc.RootElement.TryGetProperty("pathBoundary", out var boundary),
                "server_info must emit pathBoundary (camelCase) — the projection is wired but unreported.");
            Assert.AreEqual(1, boundary.GetProperty("configuredRootCount").GetInt32());
            Assert.IsTrue(boundary.GetProperty("enforcing").GetBoolean());
            Assert.AreEqual(JsonValueKind.Null, boundary.GetProperty("hint").ValueKind);
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previous;
        }
    }

    /// <summary>
    /// An unbooted host reports <c>pathBoundary: null</c> rather than omitting the field —
    /// matching the DTO's documented convention that nullable members emit explicit nulls.
    /// </summary>
    [TestMethod]
    public async Task ServerInfo_UnbootedHost_EmitsExplicitNullBoundary()
    {
        var previous = SecurityOptionsSnapshot.Value;
        try
        {
            SecurityOptionsSnapshot.Value = null;

            var json = await ServerTools.GetServerInfo(
                new FakeWorkspaceManager(),
                new FakeVersionProvider(null));

            using var doc = JsonDocument.Parse(json);
            Assert.IsTrue(doc.RootElement.TryGetProperty("pathBoundary", out var boundary));
            Assert.AreEqual(JsonValueKind.Null, boundary.ValueKind);
        }
        finally
        {
            SecurityOptionsSnapshot.Value = previous;
        }
    }

    /// <summary>
    /// Minimal fake — only <see cref="IWorkspaceManager.ListWorkspaces"/> is reached by the
    /// server_info path. Mirrors the per-file fakes in <c>ServerHeartbeatTests</c> and
    /// <c>HostProcessMetadataTests</c> rather than introducing shared test infrastructure.
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => false;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
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

    private sealed class FakeVersionProvider(string? latest) : ILatestVersionProvider
    {
        public string? GetLatestVersion() => latest;
        public VersionCheckStatus LastCheckStatus => latest is null
            ? VersionCheckStatus.Pending
            : VersionCheckStatus.Succeeded;
        public DateTime? LastCheckedAt => latest is null ? null : DateTime.UtcNow;
    }
}
