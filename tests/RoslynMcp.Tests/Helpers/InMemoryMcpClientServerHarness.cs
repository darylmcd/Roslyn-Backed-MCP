using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Hosts a real MCP client/server pair over in-memory duplex pipes for integration tests that
/// need connection-scoped client capabilities and request handlers.
/// </summary>
internal sealed class InMemoryMcpClientServerHarness(
    McpServer server,
    McpClient client,
    CancellationTokenSource serverCancellation,
    Task serverRunTask,
    IReadOnlyList<Stream> transportStreams,
    ServiceProvider? serverServices,
    string disposalFailureContext) : IAsyncDisposable
{
    private const string LegacyConnectionScopedProtocolVersion = "2025-11-25";

    public McpServer Server { get; } = server;
    public McpClient Client { get; } = client;

    public static async Task<InMemoryMcpClientServerHarness> CreateAsync(
        string transportName,
        ClientCapabilities clientCapabilities,
        McpClientHandlers clientHandlers,
        string disposalFailureContext,
        CancellationToken cancellationToken,
        string? protocolVersion = LegacyConnectionScopedProtocolVersion,
        Func<ServiceProvider>? serverServicesFactory = null,
        McpServerOptions? serverOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(transportName);
        ArgumentNullException.ThrowIfNull(clientCapabilities);
        ArgumentNullException.ThrowIfNull(clientHandlers);
        ArgumentException.ThrowIfNullOrEmpty(disposalFailureContext);

        ServiceProvider? serverServices = null;
        McpServer? server = null;
        McpClient? client = null;
        CancellationTokenSource? serverCancellation = null;
        Task? serverRunTask = null;
        var transportStreams = new List<Stream>(capacity: 4);

        try
        {
            serverServices = serverServicesFactory?.Invoke();

            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var clientToServerReadStream = clientToServer.Reader.AsStream();
            transportStreams.Add(clientToServerReadStream);
            var clientToServerWriteStream = clientToServer.Writer.AsStream();
            transportStreams.Add(clientToServerWriteStream);
            var serverToClientReadStream = serverToClient.Reader.AsStream();
            transportStreams.Add(serverToClientReadStream);
            var serverToClientWriteStream = serverToClient.Writer.AsStream();
            transportStreams.Add(serverToClientWriteStream);

            var serverTransport = new StreamServerTransport(
                clientToServerReadStream,
                serverToClientWriteStream,
                transportName,
                NullLoggerFactory.Instance);
            server = McpServer.Create(
                serverTransport,
                serverOptions ?? new McpServerOptions(),
                NullLoggerFactory.Instance,
                serverServices);
            serverCancellation = new CancellationTokenSource();
            serverRunTask = server.RunAsync(serverCancellation.Token);

            var clientTransport = new StreamClientTransport(
                clientToServerWriteStream,
                serverToClientReadStream,
                NullLoggerFactory.Instance);
            client = await McpClient.CreateAsync(
                clientTransport,
                new McpClientOptions
                {
                    // These tests directly invoke the connection-scoped server after the initialize
                    // handshake. Protocol revisions through 2025-11-25 expose capabilities there;
                    // modern request-scoped behavior is covered separately through tools/call.
                    ProtocolVersion = protocolVersion,
                    Capabilities = clientCapabilities,
                    Handlers = clientHandlers,
                },
                NullLoggerFactory.Instance,
                cancellationToken).ConfigureAwait(false);

            return new InMemoryMcpClientServerHarness(
                server,
                client,
                serverCancellation,
                serverRunTask,
                transportStreams,
                serverServices,
                disposalFailureContext);
        }
        catch (Exception initializationFailure)
        {
            var cleanupFailures = await DisposeOwnedResourcesAsync(
                client,
                server,
                serverCancellation,
                serverRunTask,
                transportStreams,
                serverServices).ConfigureAwait(false);
            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, initializationFailure);
                throw new AggregateException(
                    $"Failed to initialize and dispose the {disposalFailureContext} MCP test harness.",
                    cleanupFailures);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = await DisposeOwnedResourcesAsync(
            Client,
            Server,
            serverCancellation,
            serverRunTask,
            transportStreams,
            serverServices).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw new AggregateException(
                $"Failed to dispose the {disposalFailureContext} MCP test harness.",
                failures);
        }
    }

    private static async ValueTask<List<Exception>> DisposeOwnedResourcesAsync(
        McpClient? client,
        McpServer? server,
        CancellationTokenSource? serverCancellation,
        Task? serverRunTask,
        IReadOnlyList<Stream> transportStreams,
        ServiceProvider? serverServices)
    {
        var failures = new List<Exception>();
        if (client is not null)
        {
            await DisposeCapturingAsync(client, failures).ConfigureAwait(false);
        }

        if (serverCancellation is not null)
        {
            try
            {
                serverCancellation.Cancel();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (serverRunTask is not null)
        {
            try
            {
                await serverRunTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (serverCancellation?.IsCancellationRequested == true)
            {
                // Expected — cancelling the server's receive loop surfaces as this.
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (server is not null)
        {
            await DisposeCapturingAsync(server, failures).ConfigureAwait(false);
        }

        foreach (var stream in transportStreams)
        {
            await DisposeCapturingAsync(stream, failures).ConfigureAwait(false);
        }

        if (serverServices is not null)
        {
            await DisposeCapturingAsync(serverServices, failures).ConfigureAwait(false);
        }

        serverCancellation?.Dispose();
        return failures;
    }

    private static async ValueTask DisposeCapturingAsync(
        IAsyncDisposable disposable,
        List<Exception> failures)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }
}
