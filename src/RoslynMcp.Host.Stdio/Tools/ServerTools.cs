using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Host.Stdio.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ServerTools
{
    private static readonly ServerProcessMetadata s_directCallProcessMetadata = new();

    /// <summary>
    /// mcp-connection-session-resilience + connection-state-ready-unsatisfiable-preload
    /// + host-recycle-opacity: builds the <see cref="ConnectionStateDto"/> emitted by
    /// <c>server_info</c> and <c>server_heartbeat</c>.
    /// <para>
    /// Consumers use this block to distinguish "transport reachable" from
    /// "workspace-scoped tools will succeed":
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>idle</c> — the stdio transport is up and the server is fully initialized, but no workspace has been loaded yet. This is a terminal pre-load state (NOT a transient "initializing" step). Consumers must call <c>workspace_load</c> to advance to <c>ready</c>. Prompts that previously gated on <c>state==ready</c> should now gate on <c>state in {idle, ready}</c> when they mean "server responsive"; prompts that genuinely require a loaded workspace should still gate on <c>state==ready</c>.</description></item>
    ///   <item><description><c>ready</c> — at least one workspace session is loaded; workspace-scoped tools will resolve.</description></item>
    ///   <item><description><c>degraded</c> — reserved for future use when the server hit a startup error but is still answering the protocol. Not emitted today.</description></item>
    /// </list>
    /// <para>
    /// Prior to the <c>connection-state-ready-unsatisfiable-preload</c> fix the pre-load
    /// state was reported as <c>"initializing"</c>. That label implied a transient
    /// intermediate step and broke hard-gate prompts that polled for the transition off
    /// <c>"initializing"</c> before any workspace had been requested. The server never
    /// advances off pre-load on its own — a workspace_load call is required — so the
    /// state is now named <c>"idle"</c> to reflect reality.
    /// </para>
    /// <para>
    /// <strong>host-recycle-opacity:</strong> the FIRST probe of a freshly-started host
    /// process surfaces <c>previousStdioPid</c> / <c>previousExitedAt</c> /
    /// <c>previousRecycleReason</c> drawn from the <see cref="HostProcessMetadataStore"/>
    /// snapshot published via <see cref="HostProcessMetadataSnapshotProvider"/>. Subsequent
    /// probes omit those fields (consume-once semantics) so callers always know "this is the
    /// first probe after the recycle". Unit-test paths that construct ServerTools without
    /// publishing a snapshot get clean cold-start behavior — the optional fields are absent.
    /// </para>
    /// </summary>
    internal static ConnectionStateDto BuildConnection(
        IWorkspaceManager workspace,
        ServerProcessMetadata processMetadata)
    {
        var loadedWorkspaceCount = workspace.ListWorkspaces().Count;
        // connection-state-ready-unsatisfiable-preload: pre-load state is "idle", not
        // "initializing". The server does not auto-advance from pre-load; a workspace_load
        // call is the only transition, so the label must be terminal, not transient.
        var state = loadedWorkspaceCount >= 1 ? "ready" : "idle";

        // host-recycle-opacity: drain the previous-process snapshot exactly once. The provider
        // returns null after the first call, so subsequent probes see clean state. Tests that
        // construct ServerTools without wiring the snapshot (provider unset) get null here
        // and the optional fields are omitted from the JSON.
        var previous = HostProcessMetadataSnapshotProvider.Consume();

        return new ConnectionStateDto(
            State: state,
            LoadedWorkspaceCount: loadedWorkspaceCount,
            StdioPid: Environment.ProcessId,
            ServerStartedAt: processMetadata.StartedAtUtc.ToString("O"),
            PreviousStdioPid: previous?.StdioPid,
            PreviousExitedAtUtc: previous?.ExitedAtUtc,
            PreviousRecycleReason: previous?.RecycleReason);
    }

    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ServerInfoDto)),
     McpToolMetadata("server", "stable", true, false,
        "Inspect server capabilities, versions, and support tiers."),
     Description("Get server version, capabilities, runtime information, and loaded workspace count. workspaceCount reflects sessions at call time and may briefly lag if invoked in parallel with or immediately after workspace_load; use workspace_list for authoritative session enumeration. Prompts tier note: the response carries prompts.stable and prompts.experimental from the live catalog; all currently-exposed prompts are experimental until promoted, so stable=0 with a nonzero experimental count is expected — it is NOT a missing-surface bug. Connection readiness: the response includes a `connection` subfield with state=idle|ready|degraded, loadedWorkspaceCount, stdioPid, and serverStartedAt — use this (or the lighter `server_heartbeat` tool) to distinguish transport-reachable from workspace-loaded before calling workspace-scoped tools. State machine: `idle` = transport up but no workspace loaded (terminal pre-load state; server does NOT auto-advance — call `workspace_load` to transition to `ready`). `ready` = at least one workspace loaded; workspace-scoped tools will resolve. `degraded` = reserved for future use (not emitted today). Prompts that previously gated on `state==ready` to mean 'server responsive' should gate on `state in {idle, ready}`; prompts that genuinely require a loaded workspace should continue to gate on `state==ready`.")]
    public static Task<CallToolResult> GetServerInfo(
        IWorkspaceManager workspace,
        ILatestVersionProvider versionChecker,
        ServerProcessMetadata processMetadata)
    {
        var (version, currentSemver) = ResolveVersionInfo(typeof(ServerTools).Assembly);

        var catalogSummary = ServerSurfaceCatalog.GetSummary();
        var wsCount = workspace.ListWorkspaces().Count;
        var registeredSnapshot = SurfaceRegistrationSnapshot.Value;

        var info = new ServerInfoDto(
            Server: "roslyn-mcp",
            Version: version,
            ProductShape: "local-first",
            Runtime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Os: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            RoslynVersion: typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.GetName().Version?.ToString() ?? "unknown",
            WorkspaceCount: wsCount,
            WorkspaceCountHint: BuildWorkspaceCountHint(wsCount),
            // mcp-connection-session-resilience: explicit connection readiness so consumers
            // can distinguish transport-up from workspace-loaded without guessing via
            // workspaceCount. Same shape as `server_heartbeat` but carried inline on
            // server_info so existing pollers get it without a second round-trip.
            Connection: BuildConnection(workspace, processMetadata),
            CatalogVersion: ServerSurfaceCatalog.CatalogVersion,
            Surface: BuildSurfaceCounts(catalogSummary, registeredSnapshot),
            ResourceServerNames: new ResourceServerNameHintsDto(
                Canonical: "roslyn",
                Aliases:
                [
                    "roslyn",
                    "plugin:roslyn-mcp:roslyn",
                    "plugin_roslyn-mcp_roslyn",
                    "roslyn-mcp"
                ],
                ProbeGuidance: "Use the server name exposed by your MCP host when reading roslyn:// resources. Prefer 'roslyn' when present; otherwise match one of the aliases exactly and do not synthesize underscore variants from colon-delimited names."),
            ProductBoundaries:
            [
                "Stable support targets the local stdio host on a developer workstation.",
                "Workspace state comes from on-disk MSBuildWorkspace snapshots rather than unsaved editor buffers.",
                "Remote HTTP/SSE hosting is not part of the current stable release contract."
            ],
            Capabilities: new ServerCapabilitiesDto(Tools: true, Resources: true, Prompts: true, Logging: false, Progress: true),
            Update: BuildUpdateInfo(currentSemver, versionChecker),
            PathBoundary: BuildPathBoundary(SecurityOptionsSnapshot.Value));

        return Task.FromResult(StructuredToolResult.Create(info));
    }

    internal static Task<CallToolResult> GetServerInfo(
        IWorkspaceManager workspace,
        ILatestVersionProvider versionChecker) =>
        GetServerInfo(workspace, versionChecker, s_directCallProcessMetadata);

    /// <summary>
    /// sanctioned-roots-empty-boundary-fails-silently-until-first-call: projects the filesystem
    /// boundary into <c>server_info</c> so an unconfigured host is diagnosable before the first
    /// path call rejects. Reports the root COUNT only — never the paths, which are a server-owned
    /// security control rather than client-visible configuration.
    /// </summary>
    /// <remarks>
    /// Three coherent states. Roots configured: enforcing, no hint. Zero roots + fail-open: not
    /// enforcing, hinted because the host is deliberately unbounded and an operator should know.
    /// Zero roots + fail-closed: not enforcing and every path tool rejects — the shape that
    /// previously looked healthy right up until first use.
    /// </remarks>
    internal static PathBoundaryDto? BuildPathBoundary(SecurityOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var rootCount = options.SanctionedRoots.Count;
        var failOpen = options.PathValidationFailOpen;

        string? hint = (rootCount, failOpen) switch
        {
            (0, true) =>
                $"No sanctioned roots are configured and {SecurityOptionsEnvironmentBinder.PathValidationFailOpenVariable} " +
                "is set, so path access is unbounded. This is a temporary compatibility measure — set " +
                $"{SecurityOptionsEnvironmentBinder.SanctionedRootsVariable} and remove it.",
            (0, false) =>
                $"No sanctioned roots are configured, so path validation is fail-closed and every " +
                $"path-taking tool will reject its input. Set {SecurityOptionsEnvironmentBinder.SanctionedRootsVariable} " +
                $"to a '{Path.PathSeparator}'-delimited root list ('.' for a project-scoped host), or set " +
                $"{SecurityOptionsEnvironmentBinder.PathValidationFailOpenVariable}=true as a temporary " +
                "compatibility measure. Query-anchored solution discovery is bounded by the same list.",
            _ => null,
        };

        return new PathBoundaryDto(
            ConfiguredRootCount: rootCount,
            FailOpen: failOpen,
            Enforcing: rootCount > 0,
            Hint: hint);
    }

    /// <summary>
    /// Resolves the running assembly's informational version and its semver-only form
    /// (git-hash suffix stripped). Both flow into the <see cref="ServerInfoDto"/>: the
    /// full <paramref name="assembly"/> version as <c>Version</c>, the stripped form as
    /// the update block's <c>Current</c>.
    /// </summary>
    private static (string Version, string CurrentSemver) ResolveVersionInfo(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var currentSemver = version.Split('+')[0]; // strip git hash suffix
        return (version, currentSemver);
    }

    /// <summary>
    /// Builds the <c>workspaceCount</c> disclaimer surfaced only while no session is loaded
    /// (null once a workspace exists), so callers know a just-issued <c>workspace_load</c>
    /// may briefly still read as zero.
    /// </summary>
    private static string? BuildWorkspaceCountHint(int wsCount)
        => wsCount == 0
            ? "If you just called workspace_load, workspaceCount may still be 0 briefly; call workspace_list for authoritative session ids."
            : null;

    /// <summary>
    /// Projects the catalog summary and the (optional) runtime registration snapshot into the
    /// <see cref="ServerSurfaceCountsDto"/> emitted under <c>surface</c>.
    /// </summary>
    private static ServerSurfaceCountsDto BuildSurfaceCounts(
        SurfaceSummary catalogSummary,
        StartupDiagnostics.SurfaceRegistrationReport? registeredSnapshot)
        => new(
            Tools: new SurfaceTierCountsDto(catalogSummary.StableTools, catalogSummary.ExperimentalTools),
            Resources: new SurfaceTierCountsDto(catalogSummary.StableResources, catalogSummary.ExperimentalResources),
            Prompts: new SurfaceTierCountsDto(catalogSummary.StablePrompts, catalogSummary.ExperimentalPrompts),
            // concurrent-mcp-instances-no-tools: runtime-observed counts captured at
            // host.Build() from McpServer.ServerOptions.{Tool,Resource,Prompt}Collection.
            // A client that sees `surface.tools.registered == 0` here has reached a
            // process where WithToolsFromAssembly() found no attributed methods —
            // an unambiguous server-side failure distinct from catalog drift. Null
            // when the snapshot was not populated (unit-test paths that construct
            // ServerTools directly without booting the host).
            Registered: registeredSnapshot is null ? null : new SurfaceRegisteredCountsDto(
                Tools: registeredSnapshot.ToolsRegistered,
                Resources: registeredSnapshot.ResourcesRegistered,
                Prompts: registeredSnapshot.PromptsRegistered,
                ParityOk: registeredSnapshot.AllParityOk)
            {
                IdentityDrift = registeredSnapshot.AllParityOk ? null : new SurfaceIdentityDriftDto(
                    registeredSnapshot.ToolRegistrationDrift.Missing,
                    registeredSnapshot.ToolRegistrationDrift.Unexpected,
                    registeredSnapshot.ToolReflectionDrift.Missing,
                    registeredSnapshot.ToolReflectionDrift.Unexpected,
                    registeredSnapshot.ResourceRegistrationDrift.Missing,
                    registeredSnapshot.ResourceRegistrationDrift.Unexpected,
                    registeredSnapshot.ResourceReflectionDrift.Missing,
                    registeredSnapshot.ResourceReflectionDrift.Unexpected,
                    registeredSnapshot.PromptRegistrationDrift.Missing,
                    registeredSnapshot.PromptRegistrationDrift.Unexpected,
                    registeredSnapshot.PromptReflectionDrift.Missing,
                    registeredSnapshot.PromptReflectionDrift.Unexpected),
            });

    /// <summary>
    /// Computes the <see cref="ServerUpdateInfoDto"/> update block: reads the cached latest
    /// version + check status once, then applies the inverted-latest sanity guard.
    /// </summary>
    private static ServerUpdateInfoDto BuildUpdateInfo(string currentSemver, ILatestVersionProvider versionChecker)
    {
        // Best-effort: returns cached latest version or null if pending/failed.
        // Sanity guard: never report "update available" when the reported latest is
        // older than the running version (can happen with stale NuGet CDN cache).
        var latestVersion = versionChecker.GetLatestVersion();
        var checkStatus = versionChecker.LastCheckStatus;
        var lastCheckedAt = versionChecker.LastCheckedAt;
        var updateAvailable = latestVersion is not null
                              && Version.TryParse(currentSemver, out var currentParsed)
                              && Version.TryParse(latestVersion, out var latestParsed)
                              && latestParsed > currentParsed;

        // latest-version-status-surface: always emit the update block so operators can
        // distinguish "still checking" from "check failed/timed out" and "succeeded,
        // no newer version" even though all three keep `latest=null`.
        //
        // server-info-update-latest-inverted: only emit `latest` when the registry
        // reports a STRICTLY GREATER version than the running build. Pre-fix the
        // field surfaced any cached registry value (Jellyfin 2026-04-16: latest=1.16.0
        // while current=1.18.2 — the cached value was older). The new contract: if
        // `latest` is present, it is genuinely newer than `current`. updateAvailable
        // remains for callers that prefer the boolean.
        return new ServerUpdateInfoDto(
            Current: currentSemver,
            Latest: updateAvailable ? latestVersion : null,
            UpdateAvailable: updateAvailable,
            Command: updateAvailable ? "dotnet tool update -g Darylmcd.RoslynMcp" : null,
            CheckStatus: FormatVersionCheckStatus(checkStatus),
            LastCheckedAt: lastCheckedAt?.ToString("O"));
    }

    private static string FormatVersionCheckStatus(VersionCheckStatus status)
        => JsonSerializer.Serialize(status, JsonDefaults.Indented).Trim('"');

    /// <summary>
    /// mcp-connection-session-resilience: lightweight readiness probe. Returns only the
    /// <c>connection</c> block without the full version + catalog payload that
    /// <c>server_info</c> carries. Intended for consumers that poll the server during
    /// startup — calling <c>server_info</c> on every poll needlessly ships ~2 KB of
    /// catalog summary each time.
    /// </summary>
    [McpServerTool(Name = "server_heartbeat", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ServerHeartbeatDto)),
     McpToolMetadata("server", "stable", true, false,
        "Lightweight connection readiness probe — returns state/loadedWorkspaceCount/stdioPid/serverStartedAt without the full server_info payload."),
     Description("Return the connection readiness block only — state=idle|ready|degraded, loadedWorkspaceCount, stdioPid, and serverStartedAt. Cheaper than server_info (no version, catalog, or update metadata). State machine: `idle` = transport up but no workspace loaded (terminal pre-load state; server does NOT auto-advance — call `workspace_load` to transition to `ready`). `ready` = at least one workspace loaded; workspace-scoped tools will resolve. `degraded` = reserved for future use (not emitted today). Use this to poll for 'at least one workspace loaded' before calling workspace-scoped tools; do NOT poll waiting for `idle` to transition off its own — a `workspace_load` call is required.")]
    public static Task<CallToolResult> GetServerHeartbeat(
        IWorkspaceManager workspace,
        ServerProcessMetadata processMetadata)
    {
        var payload = new ServerHeartbeatDto(BuildConnection(workspace, processMetadata));
        return Task.FromResult(StructuredToolResult.Create(payload));
    }

    internal static Task<CallToolResult> GetServerHeartbeat(IWorkspaceManager workspace) =>
        GetServerHeartbeat(workspace, s_directCallProcessMetadata);
}
