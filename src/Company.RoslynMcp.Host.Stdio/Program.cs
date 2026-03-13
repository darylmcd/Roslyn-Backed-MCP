using Company.RoslynMcp.Roslyn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// MUST be called before any Microsoft.Build types are loaded
MsBuildInitializer.EnsureInitialized();

var builder = Host.CreateApplicationBuilder(args);

// Redirect all logging to stderr so stdout remains clean for MCP protocol
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddRoslynServices();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
