using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Stands up a real in-process MCP client/server pair over an in-memory duplex pipe for configured
/// root tests and direct tool-method fixtures that require explicit host composition.
/// </summary>
internal static class McpRootsTestServerFactory
{
    public sealed record Session(InMemoryMcpClientServerHarness Harness) : IAsyncDisposable
    {
        public McpServer Server => Harness.Server;
        public McpClient Client => Harness.Client;

        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }

    public static Task<Session> CreateWithSanctionedRootAsync(
        string sanctionedRootPath,
        CancellationToken cancellationToken,
        bool useLatestProtocol = false,
        IReadOnlyList<string>? clientRootPaths = null,
        bool allowRootExpansion = false) =>
        CreateWithSanctionedRootsAsync(
            [sanctionedRootPath],
            cancellationToken,
            useLatestProtocol,
            clientRootPaths,
            allowRootExpansion);

    /// <summary>
    /// Creates a connected client/server pair with an explicit server-owned root boundary.
    /// Optional client roots advertise the deprecated compatibility capability and may only narrow
    /// that configured boundary.
    /// </summary>
    public static async Task<Session> CreateWithSanctionedRootsAsync(
        IReadOnlyList<string> sanctionedRootPaths,
        CancellationToken cancellationToken,
        bool useLatestProtocol = false,
        IReadOnlyList<string>? clientRootPaths = null,
        bool allowRootExpansion = false)
    {
        var clientOptions = CreateClientOptions(useLatestProtocol, clientRootPaths);
        var harness = await InMemoryMcpClientServerHarness.CreateAsync(
            new InMemoryMcpHarnessOptions(
                TransportName: "mcp-roots-test",
                ClientCapabilities: clientOptions.Capabilities ?? new ClientCapabilities(),
                ClientHandlers: clientOptions.Handlers ?? new McpClientHandlers(),
                DisposalFailureContext: "MCP roots test session")
            {
                ProtocolVersion = clientOptions.ProtocolVersion,
                ServerHostFactory = (serverInput, serverOutput) =>
                {
                    var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                    hostBuilder.Logging.ClearProviders();
                    hostBuilder.Services.AddSingleton(new SecurityOptions
                    {
                        SanctionedRoots = sanctionedRootPaths,
                        AllowRootExpansion = allowRootExpansion,
                    });
                    hostBuilder.Services
                        .AddMcpServer()
                        .WithTools<McpRootsProbeTools>()
                        .WithStreamServerTransport(serverInput, serverOutput);
                    return hostBuilder.Build();
                },
            },
            cancellationToken).ConfigureAwait(false);

        return new Session(harness);
    }

    // The deprecated MCP Roots surface is intentionally isolated to this compatibility fixture,
    // matching the equally isolated production adapter.
#pragma warning disable MCP9005
    private static McpClientOptions CreateClientOptions(
        bool useLatestProtocol,
        IReadOnlyList<string>? clientRootPaths)
    {
        var options = new McpClientOptions
        {
            // Direct method tests need connection-scoped capabilities exposed by protocol
            // revisions through 2025-11-25. Modern dispatch tests opt into the latest protocol.
            ProtocolVersion = useLatestProtocol ? null : "2025-11-25",
        };

        if (clientRootPaths is null)
        {
            return options;
        }

        options.Capabilities = new ClientCapabilities
        {
            Roots = new RootsCapability(),
        };
        options.Handlers = new McpClientHandlers
        {
            RootsHandler = (_, _) => new ValueTask<ListRootsResult>(new ListRootsResult
            {
                Roots = clientRootPaths
                    .Select(path => new Root { Uri = new Uri(Path.GetFullPath(path)).AbsoluteUri })
                    .ToArray(),
            }),
        };
        return options;
    }
#pragma warning restore MCP9005
}

[McpServerToolType]
internal sealed class McpRootsProbeTools
{
    [McpServerTool(Name = "roots_boundary_probe")]
    public static async Task<string> ProbeAsync(McpServer server, string path, CancellationToken cancellationToken)
    {
        try
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server,
                path,
                cancellationToken).ConfigureAwait(false);
            return "allowed";
        }
        catch (ArgumentException ex)
        {
            return "rejected: " + ex.Message;
        }
    }
}
