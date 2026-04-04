using RoslynMcp.Host.Stdio;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// MUST be called before any Microsoft.Build types are loaded
MsBuildInitializer.EnsureInitialized();

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

// Register graceful shutdown to dispose all workspaces
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shutdown");
    logger.LogInformation("Shutting down — disposing all workspace sessions");
    var workspaceManager = host.Services.GetRequiredService<RoslynMcp.Core.Services.IWorkspaceManager>();
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
        opts = new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maxWs, MaxSourceGeneratedDocuments = opts.MaxSourceGeneratedDocuments };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS"), out var maxGen) && maxGen > 0)
        opts = new WorkspaceManagerOptions { MaxConcurrentWorkspaces = opts.MaxConcurrentWorkspaces, MaxSourceGeneratedDocuments = maxGen };
    return opts;
}

static ValidationServiceOptions BindValidationServiceOptions()
{
    var opts = new ValidationServiceOptions();
    var buildSec = Environment.GetEnvironmentVariable("ROSLYNMCP_BUILD_TIMEOUT_SECONDS");
    var testSec = Environment.GetEnvironmentVariable("ROSLYNMCP_TEST_TIMEOUT_SECONDS");

    if (int.TryParse(buildSec, out var bs) && bs > 0)
        opts = new ValidationServiceOptions { BuildTimeout = TimeSpan.FromSeconds(bs), TestTimeout = opts.TestTimeout, MaxRelatedFiles = opts.MaxRelatedFiles };
    if (int.TryParse(testSec, out var ts) && ts > 0)
        opts = new ValidationServiceOptions { BuildTimeout = opts.BuildTimeout, TestTimeout = TimeSpan.FromSeconds(ts), MaxRelatedFiles = opts.MaxRelatedFiles };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_MAX_RELATED_FILES"), out var mrf) && mrf > 0)
        opts = new ValidationServiceOptions { BuildTimeout = opts.BuildTimeout, TestTimeout = opts.TestTimeout, MaxRelatedFiles = mrf };

    return opts;
}

static PreviewStoreOptions BindPreviewStoreOptions()
{
    var opts = new PreviewStoreOptions();
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_PREVIEW_MAX_ENTRIES"), out var max) && max > 0)
        opts = new PreviewStoreOptions { MaxEntries = max, TtlMinutes = opts.TtlMinutes };
    if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_PREVIEW_TTL_MINUTES"), out var ttl) && ttl > 0)
        opts = new PreviewStoreOptions { MaxEntries = opts.MaxEntries, TtlMinutes = ttl };
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
