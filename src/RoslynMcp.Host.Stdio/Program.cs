using RoslynMcp.Host.Stdio;
using RoslynMcp.Roslyn;
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
