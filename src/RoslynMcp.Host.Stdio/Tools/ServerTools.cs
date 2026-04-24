using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ServerTools
{
    /// <summary>
    /// mcp-connection-session-resilience + connection-state-ready-unsatisfiable-preload:
    /// canonical shape for the <c>connection</c> subfield emitted by <c>server_info</c>
    /// and the <c>server_heartbeat</c> tool.
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
    /// </summary>
    private static object BuildConnection(IWorkspaceManager workspace)
    {
        var loadedWorkspaceCount = workspace.ListWorkspaces().Count;
        // connection-state-ready-unsatisfiable-preload: pre-load state is "idle", not
        // "initializing". The server does not auto-advance from pre-load; a workspace_load
        // call is the only transition, so the label must be terminal, not transient.
        var state = loadedWorkspaceCount >= 1 ? "ready" : "idle";
        return new
        {
            state,
            loadedWorkspaceCount,
            stdioPid = Environment.ProcessId,
            serverStartedAt = s_serverStartedAtUtc.ToString("O")
        };
    }

    /// <summary>
    /// Process start time captured once at host bootstrap and reused by <c>server_info</c>
    /// and <c>server_heartbeat</c>. Sourced from <see cref="Process.StartTime"/> so consumers
    /// can correlate the <c>connection.serverStartedAt</c> timestamp with OS process logs.
    /// </summary>
    private static readonly DateTime s_serverStartedAtUtc = ResolveServerStartedAtUtc();

    private static DateTime ResolveServerStartedAtUtc()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            // Some sandboxed hosts (containers without /proc, hardened Windows accounts)
            // reject StartTime reads. Fall back to "now at static ctor" — still monotonic
            // and meaningful for a single host lifetime.
            return DateTime.UtcNow;
        }
    }

    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("server", "stable", true, false,
        "Inspect server capabilities, versions, and support tiers."),
     Description("Get server version, capabilities, runtime information, and loaded workspace count. workspaceCount reflects sessions at call time and may briefly lag if invoked in parallel with or immediately after workspace_load; use workspace_list for authoritative session enumeration. Prompts tier note: the response carries prompts.stable and prompts.experimental from the live catalog; all currently-exposed prompts are experimental until promoted, so stable=0 with a nonzero experimental count is expected — it is NOT a missing-surface bug. Connection readiness: the response includes a `connection` subfield with state=idle|ready|degraded, loadedWorkspaceCount, stdioPid, and serverStartedAt — use this (or the lighter `server_heartbeat` tool) to distinguish transport-reachable from workspace-loaded before calling workspace-scoped tools. State machine: `idle` = transport up but no workspace loaded (terminal pre-load state; server does NOT auto-advance — call `workspace_load` to transition to `ready`). `ready` = at least one workspace loaded; workspace-scoped tools will resolve. `degraded` = reserved for future use (not emitted today). Prompts that previously gated on `state==ready` to mean 'server responsive' should gate on `state in {idle, ready}`; prompts that genuinely require a loaded workspace should continue to gate on `state==ready`.")]
    public static Task<string> GetServerInfo(
        IWorkspaceManager workspace,
        ILatestVersionProvider versionChecker)
    {
        var assembly = typeof(ServerTools).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "unknown";

        var catalogSummary = ServerSurfaceCatalog.GetSummary();
        var wsCount = workspace.ListWorkspaces().Count;

        // Best-effort: returns cached latest version or null if pending/failed.
        // Sanity guard: never report "update available" when the reported latest is
        // older than the running version (can happen with stale NuGet CDN cache).
        var latestVersion = versionChecker.GetLatestVersion();
        var currentSemver = version.Split('+')[0]; // strip git hash suffix
        var updateAvailable = latestVersion is not null
                              && Version.TryParse(currentSemver, out var currentParsed)
                              && Version.TryParse(latestVersion, out var latestParsed)
                              && latestParsed > currentParsed;

        var info = new
        {
            server = "roslyn-mcp",
            version,
            productShape = "local-first",
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            roslynVersion = typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.GetName().Version?.ToString() ?? "unknown",
            workspaceCount = wsCount,
            workspaceCountHint = wsCount == 0
                ? "If you just called workspace_load, workspaceCount may still be 0 briefly; call workspace_list for authoritative session ids."
                : null,
            // mcp-connection-session-resilience: explicit connection readiness so consumers
            // can distinguish transport-up from workspace-loaded without guessing via
            // workspaceCount. Same shape as `server_heartbeat` but carried inline on
            // server_info so existing pollers get it without a second round-trip.
            connection = BuildConnection(workspace),
            catalogVersion = ServerSurfaceCatalog.CatalogVersion,
            surface = new
            {
                tools = new
                {
                    stable = catalogSummary.StableTools,
                    experimental = catalogSummary.ExperimentalTools
                },
                resources = new
                {
                    stable = catalogSummary.StableResources,
                    experimental = catalogSummary.ExperimentalResources
                },
                prompts = new
                {
                    stable = catalogSummary.StablePrompts,
                    experimental = catalogSummary.ExperimentalPrompts
                },
                // concurrent-mcp-instances-no-tools: runtime-observed counts captured at
                // host.Build() from McpServer.ServerOptions.{Tool,Resource,Prompt}Collection.
                // A client that sees `surface.tools.registered == 0` here has reached a
                // process where WithToolsFromAssembly() found no attributed methods —
                // an unambiguous server-side failure distinct from catalog drift. Null
                // when the snapshot was not populated (unit-test paths that construct
                // ServerTools directly without booting the host).
                registered = SurfaceRegistrationSnapshot.Value is { } snapshot ? new
                {
                    tools = snapshot.ToolsRegistered,
                    resources = snapshot.ResourcesRegistered,
                    prompts = snapshot.PromptsRegistered,
                    parityOk = snapshot.AllParityOk
                } : null
            },
            productBoundaries = new[]
            {
                "Stable support targets the local stdio host on a developer workstation.",
                "Workspace state comes from on-disk MSBuildWorkspace snapshots rather than unsaved editor buffers.",
                "Remote HTTP/SSE hosting is not part of the current stable release contract."
            },
            capabilities = new
            {
                tools = true,
                resources = true,
                prompts = true,
                logging = true,
                progress = true
            },
            // server-info-update-latest-inverted: only emit `latest` when the registry
            // reports a STRICTLY GREATER version than the running build. Pre-fix the
            // field surfaced any cached registry value (Jellyfin 2026-04-16: latest=1.16.0
            // while current=1.18.2 — the cached value was older). The new contract: if
            // `latest` is present, it is genuinely newer than `current`. updateAvailable
            // remains for callers that prefer the boolean.
            update = latestVersion is not null ? new
            {
                current = currentSemver,
                latest = updateAvailable ? latestVersion : null,
                updateAvailable,
                command = updateAvailable ? "dotnet tool update -g Darylmcd.RoslynMcp" : (string?)null
            } : null
        };

        return Task.FromResult(JsonSerializer.Serialize(info, JsonDefaults.Indented));
    }

    /// <summary>
    /// mcp-connection-session-resilience: lightweight readiness probe. Returns only the
    /// <c>connection</c> block without the full version + catalog payload that
    /// <c>server_info</c> carries. Intended for consumers that poll the server during
    /// startup — calling <c>server_info</c> on every poll needlessly ships ~2 KB of
    /// catalog summary each time.
    /// </summary>
    [McpServerTool(Name = "server_heartbeat", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("server", "stable", true, false,
        "Lightweight connection readiness probe — returns state/loadedWorkspaceCount/stdioPid/serverStartedAt without the full server_info payload."),
     Description("Return the connection readiness block only — state=idle|ready|degraded, loadedWorkspaceCount, stdioPid, and serverStartedAt. Cheaper than server_info (no version, catalog, or update metadata). State machine: `idle` = transport up but no workspace loaded (terminal pre-load state; server does NOT auto-advance — call `workspace_load` to transition to `ready`). `ready` = at least one workspace loaded; workspace-scoped tools will resolve. `degraded` = reserved for future use (not emitted today). Use this to poll for 'at least one workspace loaded' before calling workspace-scoped tools; do NOT poll waiting for `idle` to transition off its own — a `workspace_load` call is required.")]
    public static Task<string> GetServerHeartbeat(IWorkspaceManager workspace)
    {
        var payload = new { connection = BuildConnection(workspace) };
        return Task.FromResult(JsonSerializer.Serialize(payload, JsonDefaults.Indented));
    }
}
