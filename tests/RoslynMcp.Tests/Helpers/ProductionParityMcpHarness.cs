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
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Builds an in-memory client/server pair over the full host composition with the same message
/// and request filters <c>Program.cs</c> registers, so raw-wire contract tests observe the frames a
/// real client receives. CreateAsync uses the repository boundary unless explicitly configured;
/// CreateOwnedAsync requires a fixture root and supplies its concrete owned native path policy.
/// LoadOwnedWorkspaceAsync initializes the real manager directly with captured native globals;
/// it is fixture setup and does not provide workspace_load wire coverage.
/// </summary>
internal static class ProductionParityMcpHarness
{
    /// <summary>Uses the production host composition with an explicit owned native fixture boundary.</summary>
    internal static Task<InMemoryMcpClientServerHarness> CreateOwnedAsync(
        string transportName, string? protocolVersion, string fixtureRoot) =>
        CreateAsync(transportName, protocolVersion, services =>
        {
            services.AddSingleton(new SecurityOptions { SanctionedRoots = [fixtureRoot] });
            services.AddSingleton(new ForkInvocationPathPolicy(new OwnedTestWorkspacePathPolicy([fixtureRoot])));
        });

    /// <summary>Preloads fixture state in the harness's real manager with explicit native MSBuild globals.</summary>
    internal static async Task<string> LoadOwnedWorkspaceAsync(
        InMemoryMcpClientServerHarness harness, string path, CancellationToken ct)
    {
        var manager = harness.ServerServices.GetRequiredService<IWorkspaceManager>();
        Assert.IsInstanceOfType<WorkspaceManager>(manager, "The production contract must resolve the real provider-owned manager for native fixture setup.");
        var status = await ((WorkspaceManager)manager).LoadAsync(path,
            OwnedNativeLoadContext.Properties(path, new Dictionary<string, string>()), ct);
        return status.WorkspaceId;
    }

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
