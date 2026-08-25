using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Configuration;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

// mcp-stdio-console-flush-on-exit: belt-and-suspenders synchronous flush hook that fires
// on every process-exit path (graceful, abrupt, AppDomain unload). Pre-fix the host
// flushed in the ApplicationStopping callback + after RunAsync returns, but on stdin-EOF
// the SDK transport could exit fast enough that buffered MCP JSON responses were lost
// before the async FlushAsync completed (IT-Chat-Bot 2026-04-13 §9.4: clients received
// 0 bytes). The ProcessExit handler runs synchronously during runtime teardown — anything
// still in the stdout buffer at that moment makes it to the pipe.
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    StdioShutdownFlusher.Flush(Console.Out, Console.Error.WriteLine, "process-exit");
};

var builder = Host.CreateApplicationBuilder(args);
var toolTierSelection = ToolTierSelection.Parse(ReadEnv(ToolTierSelection.EnvironmentVariableName));
var observabilityOptions = ServerObservabilityOptions.Parse(
    ReadEnv(ServerObservabilityOptions.EnvironmentVariableName));

// Redirect all logging to stderr so stdout remains clean for MCP protocol
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Bind options from environment variables (hardcoded defaults used when env vars are absent)
// then register the entire host composition root via AddRoslynMcpHostServices — the same
// extension method consumed by StartupDiagnosticsTests and ToolDiResolutionTests so the
// production and test DI graphs cannot drift.
builder.Services.AddRoslynMcpHostServices(
    BindWorkspaceManagerOptions(),
    BindValidationServiceOptions(),
    BindPreviewStoreOptions(),
    BindExecutionGateOptions(),
    BindSecurityOptions(),
    BindScriptingServiceOptions());
builder.Services.AddSingleton(observabilityOptions);
builder.Services.AddSingleton<IServerObservabilitySink>(_ => observabilityOptions.Sink switch
{
    ServerObservabilitySinkKind.Disabled => new DisabledServerObservabilitySink(),
    ServerObservabilitySinkKind.Stderr => new StderrServerObservabilitySink(),
    _ => throw new InvalidOperationException("Unsupported server observability sink."),
});
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "roslyn-mcp",
            Title = "Roslyn MCP Server",
            Version = typeof(RoslynMcp.Host.Stdio.HostAssemblyMarker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        };
        options.ServerInstructions = ServerInstructions.For(toolTierSelection);
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly()
    .WithMessageFilters(messageFilters =>
        messageFilters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
    // Single error-handling and observability boundary for every tools/call dispatch.
    // See ai_docs/references/mcp-server-best-practices.md § 2-3. Replaces the legacy
    // per-handler ToolErrorHandler.ExecuteAsync(...) wrapper so that pre-binding
    // failures (missing/unknown required parameter, JSON deserialization of arguments)
    // surface the same structured CallToolResult { IsError = true } envelope as any
    // exception thrown inside a handler body. Requires SDK PR csharp-sdk#844 (shipped
    // in 0.4.0-preview.3, carried into 1.x) so filters observe binding-stage
    // ArgumentException / JsonException propagation.
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(StaticListResultFilter.CreateTools);
        requestFilters.AddListPromptsFilter(StaticListResultFilter.CreatePrompts);
        requestFilters.AddListResourcesFilter(StaticListResultFilter.CreateResources);
        requestFilters.AddListResourceTemplatesFilter(StaticListResultFilter.CreateResourceTemplates);
        requestFilters.AddReadResourceFilter(ResourceReadResultFilter.Create);
        requestFilters.AddCallToolFilter(StructuredCallToolFilter.Create);
        // Single error-handling and observability boundary for every prompts/get dispatch.
        // Unexpected prompt failures ride the JSON-RPC error channel as a sanitized
        // InternalError (-32603) instead of being returned as successful user-role prompt
        // messages carrying raw exception text. See GetPromptErrorFilter.
        requestFilters.AddGetPromptFilter(GetPromptErrorFilter.Create);
    });
builder.Services.AddRoslynMcpSurfaceRegistrationPolicy(toolTierSelection);

var host = builder.Build();

// concurrent-mcp-instances-no-tools: cross-check SDK-registered vs reflection vs
// catalog surface counts and publish the snapshot for server_info. When multiple
// roslynmcp processes start in parallel and the client reports "no tools available"
// on one, each process's stderr carries a "Startup surface: …" line that tells the
// operator whether the problem is server-side (registered=0 here) or client-side
// (registered=N on every instance but the host presented an empty tool list).
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var surfaceReport = StartupDiagnostics.Capture(host.Services, typeof(RoslynMcp.Host.Stdio.HostAssemblyMarker).Assembly);
var assemblyVersion = typeof(RoslynMcp.Host.Stdio.HostAssemblyMarker).Assembly
    .GetName().Version?.ToString() ?? "unknown";
StartupDiagnostics.LogStartup(startupLogger, surfaceReport, assemblyVersion);
SurfaceRegistrationSnapshot.Value = surfaceReport;

// host-recycle-opacity: read the previous host process's exit metadata (if any) from disk
// and publish it for the FIRST server_info / server_heartbeat probe to surface. The
// provider's Consume() drains the snapshot exactly once — subsequent probes see clean state.
// The on-disk record is deleted by LoadPrevious() so we never replay a stale snapshot across
// multiple processes. Cold start with no prior record publishes null, which the provider
// treats as "no previous-* fields ever". The store is also kept around so the
// ApplicationStopping handler can write the current process's exit metadata on shutdown.
var hostProcessMetadataLogger = host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("HostProcessMetadata");
var hostProcessMetadataStore = new HostProcessMetadataStore(hostProcessMetadataLogger);
var previousHostMetadata = hostProcessMetadataStore.LoadPrevious();
HostProcessMetadataSnapshotProvider.Publish(previousHostMetadata);

// mcp-error-category-workspace-evicted-on-host-recycle: also publish the recycle signal
// to the WorkspaceEvictionRegistry so WorkspaceManager.GetRequiredSession can throw a
// structured WorkspaceEvictedException on workspace lookups for ids owned by the prior
// process. Unlike HostProcessMetadataSnapshotProvider (consume-once for server_info),
// the registry signal must persist for the lifetime of the process — every workspace
// lookup miss in a recycled host needs to consult it. ServerProcessMetadata is the single
// process-start authority shared with server_info and server_heartbeat.
var serverProcessMetadata = host.Services.GetRequiredService<ServerProcessMetadata>();
RoslynMcp.Core.Services.WorkspaceEvictionRegistry.PublishRecycleContext(
    serverProcessMetadata.StartedAtUtc,
    previousHostMetadata?.RecycleReason);

// FLAG-D: Emit an Information event when the host starts with no loaded workspaces.
// Operational logs remain on stderr; clients inspect server_info/server_heartbeat for state.
var startupWorkspaceManager = host.Services.GetRequiredService<IWorkspaceManager>();
// root-expansion-grant-revoke-on-lifecycle-event: bind grant ownership to the workspace
// lifecycle rather than to a single close tool. WorkspaceClosed also covers cap-pressure LRU
// eviction and host disposal, so every terminal session path revokes the authorization grant.
startupWorkspaceManager.WorkspaceClosed += RootExpansionGrantRegistry.Revoke;
if (startupWorkspaceManager.ListWorkspaces().Count == 0)
{
    startupLogger.LogInformation(
        "Roslyn MCP host started with zero loaded workspaces. " +
        "If this is a transparent subprocess restart, call workspace_load to rehydrate the prior session.");
}

// sanctioned-roots-empty-boundary-fails-silently-until-first-call: an unconfigured boundary is
// fail-closed, so EVERY path-taking tool rejects — but nothing said so until the first call threw.
// Warn at startup for the same reason as the zero-workspaces notice above. Only the genuinely
// broken shape warns; fail-open is an explicit operator choice and stays quiet.
var startupSecurityOptions = host.Services.GetRequiredService<SecurityOptions>();
SecurityOptionsSnapshot.Value = startupSecurityOptions;
// The remediation text is derived from the SAME projection server_info reports, so the startup
// warning and the tool response can never drift apart — and the message is unit-tested there.
if (ServerTools.BuildPathBoundary(startupSecurityOptions)
    is { Enforcing: false, FailOpen: false, Hint: { } boundaryHint })
{
    startupLogger.LogWarning("Filesystem boundary is not configured. {BoundaryHint}", boundaryHint);
}

// Register graceful-shutdown side effects. DI/container disposal remains the sole owner of
// IWorkspaceManager teardown after in-flight hosted work has completed.
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shutdown");
    logger.LogInformation("Shutting down — persisting host metadata");

    // host-recycle-opacity: persist current-process exit metadata so the NEXT host process
    // can surface previousStdioPid / previousExitedAt / previousRecycleReason on its first
    // probe. We tag this path "graceful" — the only call site here is the
    // ApplicationStopping hook, which only fires on a clean shutdown. Future watchdog /
    // idle-eviction code that knows specifically why it terminated will pass its own reason.
    hostProcessMetadataStore.WriteCurrent(recycleReason: "graceful");

    // Flush stdout so buffered MCP JSON responses are delivered before the process exits.
    // Without this, non-SDK clients using bash pipes may receive 0 bytes on stdout.
    StdioShutdownFlusher.Flush(Console.Out, Console.Error.WriteLine, "application-stopping");
});

// compile-check-not-connected-raw-transport-error-envelope (path b): if the SDK's
// stdio transport write layer throws InvalidOperationException("Not connected") or
// IOException AFTER the filter has already returned a CallToolResult, the exception
// escapes RunAsync entirely. Catch it here so the process exits with a clean log
// rather than an unhandled-exception crash that loses the exit-metadata write and
// the stdout flush. This is a graceful-disconnect signal — not a fatal fault — so
// we log at Warning and allow the normal ApplicationStopping shutdown path to run.
try
{
    await host.RunAsync();
}
catch (Exception ex) when (
    ex is IOException ||
    (ex is InvalidOperationException && ex.Message.Contains("Not connected", StringComparison.OrdinalIgnoreCase)))
{
    // Transport-layer disconnect: the MCP client closed the pipe. Log to stderr
    // (stdout is likely closed) and let the process exit gracefully. The
    // ApplicationStopping handler already ran or will run via the ProcessExit hook.
    Console.Error.WriteLine(
        $"[roslyn-mcp] Transport disconnected during RunAsync ({ex.GetType().Name}: {ex.Message}). " +
        "This is normal on client-side session close.");
}

// Belt-and-suspenders: flush stdout after the host stops in case the
// ApplicationStopping handler didn't run (e.g., on abrupt shutdown).
// Both the sync and async overloads — sync ensures the buffer is drained before
// any subsequent disposal/IO; async re-flushes any encoder writes that batched
// behind the sync call. The ProcessExit handler at the top of this file is the
// final fallback for stdin-EOF cases where RunAsync may not return cleanly.
StdioShutdownFlusher.Flush(Console.Out, Console.Error.WriteLine, "post-run");
await StdioShutdownFlusher.FlushAsync(Console.Out, Console.Error.WriteLine, "post-run-async");

static WorkspaceManagerOptions BindWorkspaceManagerOptions()
{
    var opts = new WorkspaceManagerOptions();
    if (int.TryParse(ReadEnv("ROSLYNMCP_MAX_WORKSPACES"), out var maxWs) && maxWs > 0)
        opts = opts with { MaxConcurrentWorkspaces = maxWs };
    if (int.TryParse(ReadEnv("ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS"), out var maxGen) && maxGen > 0)
        opts = opts with { MaxSourceGeneratedDocuments = maxGen };
    // dr-9-10-initial-does-not-wait-for-concurrent-to-finaliz: upper bound (ms) for the
    // restore-race wait inside WorkspaceManager.LoadAsync. 0 disables. Accept 0 explicitly
    // so operators can opt out at runtime without editing code.
    if (int.TryParse(ReadEnv("ROSLYNMCP_RESTORE_RACE_WAIT_MS"), out var waitMs) && waitMs >= 0)
        opts = opts with { RestoreRaceWaitMs = waitMs };
    return opts;
}

static ValidationServiceOptions BindValidationServiceOptions()
{
    var opts = new ValidationServiceOptions();
    var buildSec = ReadEnv("ROSLYNMCP_BUILD_TIMEOUT_SECONDS");
    var testSec = ReadEnv("ROSLYNMCP_TEST_TIMEOUT_SECONDS");
    var vulnSec = ReadEnv("ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS");
    var revertSec = ReadEnv("ROSLYNMCP_APPLY_REVERT_TIMEOUT_SECONDS");
    var gitStatusSec = ReadEnv("ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS");

    if (int.TryParse(buildSec, out var bs) && bs > 0)
        opts = opts with { BuildTimeout = TimeSpan.FromSeconds(bs) };
    if (int.TryParse(testSec, out var ts) && ts > 0)
        opts = opts with { TestTimeout = TimeSpan.FromSeconds(ts) };
    if (int.TryParse(ReadEnv("ROSLYNMCP_MAX_RELATED_FILES"), out var mrf) && mrf > 0)
        opts = opts with { MaxRelatedFiles = mrf };
    if (int.TryParse(vulnSec, out var vs) && vs > 0)
        opts = opts with { VulnerabilityScanTimeout = TimeSpan.FromSeconds(vs) };
    if (int.TryParse(revertSec, out var rs) && rs > 0)
        opts = opts with { ApplyRevertTimeout = TimeSpan.FromSeconds(rs) };
    if (int.TryParse(gitStatusSec, out var gss) && gss > 0)
        opts = opts with { GitStatusTimeout = TimeSpan.FromSeconds(gss) };

    return opts;
}

static PreviewStoreOptions BindPreviewStoreOptions()
{
    var opts = new PreviewStoreOptions();
    if (int.TryParse(ReadEnv("ROSLYNMCP_PREVIEW_MAX_ENTRIES"), out var max) && max > 0)
        opts = opts with { MaxEntries = max };
    if (int.TryParse(ReadEnv("ROSLYNMCP_PREVIEW_TTL_MINUTES"), out var ttl) && ttl > 0)
        opts = opts with { TtlMinutes = ttl };
    var persistDir = ReadEnv("ROSLYNMCP_PREVIEW_PERSIST_DIR");
    if (!string.IsNullOrWhiteSpace(persistDir))
        opts = opts with { PersistDirectory = persistDir };
    return opts;
}

static ExecutionGateOptions BindExecutionGateOptions()
{
    var maxReqVal = 120;
    var winSecVal = 60;
    var reqSecVal = 120;
    if (int.TryParse(ReadEnv("ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS"), out var maxReq) && maxReq > 0)
        maxReqVal = maxReq;
    if (int.TryParse(ReadEnv("ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS"), out var winSec) && winSec > 0)
        winSecVal = winSec;
    if (int.TryParse(ReadEnv("ROSLYNMCP_REQUEST_TIMEOUT_SECONDS"), out var reqSec) && reqSec > 0)
        reqSecVal = reqSec;
    var onStale = HostEnvironmentOptions.ParseStalenessPolicy(ReadEnv("ROSLYNMCP_ON_STALE"));
    return new ExecutionGateOptions
    {
        RateLimitMaxRequests = maxReqVal,
        RateLimitWindow = TimeSpan.FromSeconds(winSecVal),
        RequestTimeout = TimeSpan.FromSeconds(reqSecVal),
        OnStale = onStale,
    };
}

static SecurityOptions BindSecurityOptions()
{
    return SecurityOptionsEnvironmentBinder.Bind(
        ReadEnv(SecurityOptionsEnvironmentBinder.SanctionedRootsVariable),
        ReadEnv(SecurityOptionsEnvironmentBinder.PathValidationFailOpenVariable),
        ReadEnv(SecurityOptionsEnvironmentBinder.AllowRootExpansionVariable));
}

static ScriptingServiceOptions BindScriptingServiceOptions()
{
    return ScriptingOptionsEnvironmentBinder.Bind(Environment.GetEnvironmentVariable);
}

// Reads a ROSLYNMCP_* environment variable, but treats unresolved Claude Code
// `${user_config.KEY}` placeholders as "unset" so the in-source default applies.
// Claude Code substitutes placeholders before spawning the server when the user has
// configured the matching key; if the user never set it, the raw `${user_config.KEY}`
// string arrives as the env value and would otherwise poison every int.TryParse /
// bool.TryParse call with no log signal. We log once per unresolved key to stderr.
static string? ReadEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrEmpty(value)
        && value.StartsWith("${user_config.", StringComparison.Ordinal)
        && value.EndsWith("}", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"[roslyn-mcp] Ignoring unresolved Claude Code user-config placeholder for {name} " +
            $"(received literal '{value}'). Using the in-source default for this session; " +
            $"set the value in the plugin's user config to enable the override.");
        return null;
    }
    return value;
}
