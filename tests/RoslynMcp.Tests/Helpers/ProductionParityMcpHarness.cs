using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Builds an in-memory client/server pair over the full host composition with the same message
/// and request filters <c>Program.cs</c> registers, so raw-wire contract tests observe the frames a
/// real client receives. The filesystem boundary is the repository root.
/// </summary>
internal static class ProductionParityMcpHarness
{
    /// <param name="transportName">Label for the in-memory transport and disposal diagnostics.</param>
    /// <param name="protocolVersion">Requested protocol version, or <see langword="null"/> for the latest.</param>
    /// <param name="configureServices">
    /// Optional overrides applied after the host services are registered; a later singleton
    /// registration replaces the host default (for example a deterministic version provider).
    /// </param>
    public static Task<InMemoryMcpClientServerHarness> CreateAsync(
        string transportName,
        string? protocolVersion,
        Action<IServiceCollection>? configureServices = null)
    {
        var hostAssembly = typeof(HostAssemblyMarker).Assembly;
        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.ClearProviders());
        services.AddRoslynMcpHostServices(
            new WorkspaceManagerOptions(),
            new ValidationServiceOptions(),
            new PreviewStoreOptions(),
            new ExecutionGateOptions(),
            new SecurityOptions { SanctionedRoots = [TestFixtureFileSystem.FindRepositoryRoot()] },
            new ScriptingServiceOptions());
        configureServices?.Invoke(services);
        services
            .AddMcpServer(static options =>
            {
                options.ServerInfo = new Implementation { Name = "roslyn-mcp-test", Version = "1.0.0" };
            })
            .WithToolsFromAssembly(hostAssembly)
            .WithResourcesFromAssembly(hostAssembly)
            .WithPromptsFromAssembly(hostAssembly)
            .WithMessageFilters(static filters => filters.AddIncomingFilter(RequestCorrelationMessageFilter.Create))
            .WithRequestFilters(static filters =>
            {
                filters.AddListToolsFilter(StaticListResultFilter.CreateTools);
                filters.AddListPromptsFilter(StaticListResultFilter.CreatePrompts);
                filters.AddListResourcesFilter(StaticListResultFilter.CreateResources);
                filters.AddListResourceTemplatesFilter(StaticListResultFilter.CreateResourceTemplates);
                filters.AddReadResourceFilter(ResourceReadResultFilter.Create);
                filters.AddCallToolFilter(StructuredCallToolFilter.Create);
                filters.AddGetPromptFilter(GetPromptErrorFilter.Create);
            });
        services.AddRoslynMcpSurfaceRegistrationPolicy(ToolTierSelection.All);

        var provider = services.BuildServiceProvider();
        return InMemoryMcpClientServerHarness.CreateAsync(
            transportName: transportName,
            clientCapabilities: new ClientCapabilities(),
            clientHandlers: new McpClientHandlers(),
            disposalFailureContext: transportName,
            cancellationToken: CancellationToken.None,
            protocolVersion: protocolVersion,
            serverOptions: provider.GetRequiredService<IOptions<McpServerOptions>>().Value,
            serverServicesFactory: () => provider,
            captureServerMessages: true);
    }
}
