using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

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
builder.Services.AddSingleton<RoslynMcp.Host.Stdio.Services.NuGetVersionChecker>();

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
    .WithPromptsFromAssembly();

var host = builder.Build();

// Wire the MCP logging bridge to the server instance
var server = host.Services.GetRequiredService<McpServer>();
mcpLoggingProvider.SetServer(server);

// FLAG-D: Emit a structured Information event when the host starts with no loaded workspaces.
// Clients that surface MCP `notifications/message` (via McpLoggingProvider) will see this
// proactively after a transparent subprocess restart instead of discovering the missing
// workspace tool-by-tool through cascading KeyNotFoundException errors.
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
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
});

await host.RunAsync();

static WorkspaceManagerOptions BindWorkspaceManagerOptions()
{
    var opts = new WorkspaceManagerOptions();
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_MAX_WORKSPACES"), out var maxWs) && maxWs > 0)
        opts = opts with { MaxConcurrentWorkspaces = maxWs };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS"), out var maxGen) && maxGen > 0)
        opts = opts with { MaxSourceGeneratedDocuments = maxGen };
    return opts;
}

static ValidationServiceOptions BindValidationServiceOptions()
{
    var opts = new ValidationServiceOptions();
    var buildSec = Environment.GetEnvironmentVariable("ROSLYNMCP_BUILD_TIMEOUT_SECONDS");
    var testSec = Environment.GetEnvironmentVariable("ROSLYNMCP_TEST_TIMEOUT_SECONDS");
    var vulnSec = Environment.GetEnvironmentVariable("ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS");

    if (int.TryParse(buildSec, out var bs) && bs > 0)
        opts = opts with { BuildTimeout = TimeSpan.FromSeconds(bs) };
    if (int.TryParse(testSec, out var ts) && ts > 0)
        opts = opts with { TestTimeout = TimeSpan.FromSeconds(ts) };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_MAX_RELATED_FILES"), out var mrf) && mrf > 0)
        opts = opts with { MaxRelatedFiles = mrf };
    if (int.TryParse(vulnSec, out var vs) && vs > 0)
        opts = opts with { VulnerabilityScanTimeout = TimeSpan.FromSeconds(vs) };

    return opts;
}

static PreviewStoreOptions BindPreviewStoreOptions()
{
    var opts = new PreviewStoreOptions();
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_PREVIEW_MAX_ENTRIES"), out var max) && max > 0)
        opts = opts with { MaxEntries = max };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_PREVIEW_TTL_MINUTES"), out var ttl) && ttl > 0)
        opts = opts with { TtlMinutes = ttl };
    return opts;
}

static ExecutionGateOptions BindExecutionGateOptions()
{
    var maxReqVal = 120;
    var winSecVal = 60;
    var reqSecVal = 120;
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS"), out var maxReq) && maxReq > 0)
        maxReqVal = maxReq;
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS"), out var winSec) && winSec > 0)
        winSecVal = winSec;
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_REQUEST_TIMEOUT_SECONDS"), out var reqSec) && reqSec > 0)
        reqSecVal = reqSec;
    return new ExecutionGateOptions
    {
        RateLimitMaxRequests = maxReqVal,
        RateLimitWindow = TimeSpan.FromSeconds(winSecVal),
        RequestTimeout = TimeSpan.FromSeconds(reqSecVal)
    };
}

static SecurityOptions BindSecurityOptions()
{
    var raw = Environment.GetEnvironmentVariable("ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN");
    if (bool.TryParse(raw, out var failOpen))
        return new SecurityOptions { PathValidationFailOpen = failOpen };
    return new SecurityOptions();
}

static ScriptingServiceOptions BindScriptingServiceOptions()
{
    var opts = new ScriptingServiceOptions();
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS"), out var t) && t > 0)
        opts = opts with { TimeoutSeconds = t };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_HEARTBEAT_MS"), out var hb) && hb > 0)
        opts = opts with { HeartbeatIntervalMs = hb };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_STUCK_WARNING_SECONDS"), out var sw) && sw > 0)
        opts = opts with { StuckWarningSeconds = sw };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS"), out var wg) && wg >= 0)
        opts = opts with { WatchdogGraceSeconds = wg };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_WATCHDOG_REPEAT_SECONDS"), out var wr) && wr > 0)
        opts = opts with { WatchdogRepeatSeconds = wr };
    // FLAG-5C: bound concurrent script evaluations so leaked infinite loops cannot accumulate.
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_MAX_CONCURRENT"), out var mc) && mc > 0)
        opts = opts with { MaxConcurrentEvaluations = mc };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS"), out var sl) && sl > 0)
        opts = opts with { ConcurrencySlotAcquireTimeoutSeconds = sl };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_MAX_ABANDONED"), out var ma) && ma > 0)
        opts = opts with { MaxAbandonedEvaluations = ma };
    return opts;
}
