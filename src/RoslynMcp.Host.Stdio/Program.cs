using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;
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
    try { Console.Out.Flush(); } catch { /* shutdown — never throw */ }
};

var builder = Host.CreateApplicationBuilder(args);

// Redirect all logging to stderr so stdout remains clean for MCP protocol
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Register the MCP logging bridge that forwards log events to the client
var mcpLoggingProvider = new McpLoggingProvider();
builder.Logging.AddProvider(mcpLoggingProvider);

// Bind options from environment variables (hardcoded defaults used when env vars are absent)
builder.Services.AddSingleton(BindWorkspaceManagerOptions());
builder.Services.AddSingleton(BindValidationServiceOptions());
builder.Services.AddSingleton(BindPreviewStoreOptions());
builder.Services.AddSingleton(BindExecutionGateOptions());
builder.Services.AddSingleton(BindSecurityOptions());
builder.Services.AddSingleton(BindScriptingServiceOptions());

builder.Services.AddSingleton<HttpClient>();
// Register NuGetVersionChecker against both its concrete type and its ILatestVersionProvider
// interface. The concrete registration is needed for anything that imports the concrete type
// directly; the interface registration is what MCP tool methods (e.g. server_info) inject.
// v1.19.0 shipped with only the concrete registration, which caused the MCP SDK's parameter
// binder to fail to resolve `ILatestVersionProvider versionChecker` and leak the service
// parameter into the tool schema as a required user-supplied argument — breaking server_info
// (fix: di-register-latest-version-provider).
builder.Services.AddSingleton<RoslynMcp.Host.Stdio.Services.NuGetVersionChecker>();
builder.Services.AddSingleton<RoslynMcp.Host.Stdio.Services.ILatestVersionProvider>(
    sp => sp.GetRequiredService<RoslynMcp.Host.Stdio.Services.NuGetVersionChecker>());

builder.Services.AddRoslynServices();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "roslyn-mcp",
            Title = "Roslyn MCP Server",
            Version = typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly()
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
        requestFilters.AddCallToolFilter(StructuredCallToolFilter.Create);
    });

var host = builder.Build();

// Wire the MCP logging bridge to the server instance
var server = host.Services.GetRequiredService<McpServer>();
mcpLoggingProvider.SetServer(server);

// concurrent-mcp-instances-no-tools: cross-check SDK-registered vs reflection vs
// catalog surface counts and publish the snapshot for server_info. When multiple
// roslynmcp processes start in parallel and the client reports "no tools available"
// on one, each process's stderr carries a "Startup surface: …" line that tells the
// operator whether the problem is server-side (registered=0 here) or client-side
// (registered=N on every instance but the host presented an empty tool list).
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var surfaceReport = StartupDiagnostics.Capture(host.Services, typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly);
var assemblyVersion = typeof(RoslynMcp.Host.Stdio.McpLoggingProvider).Assembly
    .GetName().Version?.ToString() ?? "unknown";
StartupDiagnostics.LogStartup(startupLogger, surfaceReport, assemblyVersion);
SurfaceRegistrationSnapshot.Value = surfaceReport;

// FLAG-D: Emit a structured Information event when the host starts with no loaded workspaces.
// Clients that surface MCP `notifications/message` (via McpLoggingProvider) will see this
// proactively after a transparent subprocess restart instead of discovering the missing
// workspace tool-by-tool through cascading KeyNotFoundException errors.
var startupWorkspaceManager = host.Services.GetRequiredService<IWorkspaceManager>();
if (startupWorkspaceManager.ListWorkspaces().Count == 0)
{
    startupLogger.LogInformation(
        "Roslyn MCP host started with zero loaded workspaces. " +
        "If this is a transparent subprocess restart, call workspace_load to rehydrate the prior session.");
}

// Register graceful shutdown to dispose all workspaces
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shutdown");
    logger.LogInformation("Shutting down — disposing all workspace sessions");
    var workspaceManager = host.Services.GetRequiredService<IWorkspaceManager>();
    if (workspaceManager is IDisposable disposable)
    {
        disposable.Dispose();
    }
    // Flush stdout so buffered MCP JSON responses are delivered before the process exits.
    // Without this, non-SDK clients using bash pipes may receive 0 bytes on stdout.
    Console.Out.Flush();
});

await host.RunAsync();

// Belt-and-suspenders: flush stdout after the host stops in case the
// ApplicationStopping handler didn't run (e.g., on abrupt shutdown).
// Both the sync and async overloads — sync ensures the buffer is drained before
// any subsequent disposal/IO; async re-flushes any encoder writes that batched
// behind the sync call. The ProcessExit handler at the top of this file is the
// final fallback for stdin-EOF cases where RunAsync may not return cleanly.
Console.Out.Flush();
await Console.Out.FlushAsync();

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

    if (int.TryParse(buildSec, out var bs) && bs > 0)
        opts = opts with { BuildTimeout = TimeSpan.FromSeconds(bs) };
    if (int.TryParse(testSec, out var ts) && ts > 0)
        opts = opts with { TestTimeout = TimeSpan.FromSeconds(ts) };
    if (int.TryParse(ReadEnv("ROSLYNMCP_MAX_RELATED_FILES"), out var mrf) && mrf > 0)
        opts = opts with { MaxRelatedFiles = mrf };
    if (int.TryParse(vulnSec, out var vs) && vs > 0)
        opts = opts with { VulnerabilityScanTimeout = TimeSpan.FromSeconds(vs) };

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
    var onStale = StalenessPolicy.AutoReload;
    var onStaleRaw = ReadEnv("ROSLYNMCP_ON_STALE");
    if (!string.IsNullOrWhiteSpace(onStaleRaw))
    {
        onStale = onStaleRaw.Trim().ToLowerInvariant() switch
        {
            "auto-reload" or "autoreload" => StalenessPolicy.AutoReload,
            "warn" => StalenessPolicy.Warn,
            "off" or "none" or "disabled" => StalenessPolicy.Off,
            _ => StalenessPolicy.AutoReload,
        };
    }
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
    var raw = ReadEnv("ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN");
    if (bool.TryParse(raw, out var failOpen))
        return new SecurityOptions { PathValidationFailOpen = failOpen };
    return new SecurityOptions();
}

static ScriptingServiceOptions BindScriptingServiceOptions()
{
    var opts = new ScriptingServiceOptions();
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS"), out var t) && t > 0)
        opts = opts with { TimeoutSeconds = t };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_HEARTBEAT_MS"), out var hb) && hb > 0)
        opts = opts with { HeartbeatIntervalMs = hb };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_STUCK_WARNING_SECONDS"), out var sw) && sw > 0)
        opts = opts with { StuckWarningSeconds = sw };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS"), out var wg) && wg >= 0)
        opts = opts with { WatchdogGraceSeconds = wg };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_WATCHDOG_REPEAT_SECONDS"), out var wr) && wr > 0)
        opts = opts with { WatchdogRepeatSeconds = wr };
    // FLAG-5C: bound concurrent script evaluations so leaked infinite loops cannot accumulate.
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_MAX_CONCURRENT"), out var mc) && mc > 0)
        opts = opts with { MaxConcurrentEvaluations = mc };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS"), out var sl) && sl > 0)
        opts = opts with { ConcurrencySlotAcquireTimeoutSeconds = sl };
    if (int.TryParse(ReadEnv("ROSLYNMCP_SCRIPT_MAX_ABANDONED"), out var ma) && ma > 0)
        opts = opts with { MaxAbandonedEvaluations = ma };
    return opts;
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
