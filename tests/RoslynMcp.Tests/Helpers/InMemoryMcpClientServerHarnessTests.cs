using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

[TestClass]
public sealed class InMemoryMcpClientServerHarnessTests
{
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task CreateAsync_DelayedDiscovery_DoesNotDowngradeToLegacyHandshake()
    {
        var discoveryCount = 0;
        var serverOptions = new McpServerOptions
        {
            Filters = new McpServerFilters
            {
                Message = new McpMessageFilters
                {
                    IncomingFilters = [next => async (context, cancellationToken) =>
                    {
                        if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ServerDiscover })
                        {
                            Interlocked.Increment(ref discoveryCount);
                            // Cross the SDK's automatic fallback threshold, rather than
                            // relying on incidental machine load to delay discovery.
                            await Task.Delay(new McpClientOptions().DiscoverProbeTimeout + TimeSpan.FromSeconds(1),
                                cancellationToken);
                        }
                        await next(context, cancellationToken);
                    }],
                },
            },
        };
        async Task AssertProtocolAsync(string? requested, string expected)
        {
            await using var harness = await InMemoryMcpClientServerHarness.CreateAsync(
                transportName: "delayed-discovery-" + expected,
                clientCapabilities: new ClientCapabilities { Elicitation = new ElicitationCapability() },
                clientHandlers: new McpClientHandlers(),
                disposalFailureContext: "delayed-discovery-" + expected,
                cancellationToken: CancellationToken.None,
                protocolVersion: requested,
                serverOptions: requested is null ? serverOptions : null);

            Assert.AreEqual(expected, harness.Client.NegotiatedProtocolVersion);
            if (requested is null)
                Assert.IsNull(harness.Server.ClientCapabilities,
                    "A delayed discovery response must retain modern request-scoped capabilities.");
            else
                Assert.IsNotNull(harness.Server.ClientCapabilities,
                    "The concurrent legacy peer must retain its own capability snapshot.");
        }

        for (var iteration = 0; iteration < 3; iteration++)
        {
            await Task.WhenAll(
                AssertProtocolAsync(null, RequestProtocolFeatureGate.July2026ProtocolVersion),
                AssertProtocolAsync("2025-11-25", "2025-11-25"));
        }
        Assert.AreEqual(3, discoveryCount);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task CreateAsync_WhenClientInitializationIsCancelled_DisposesServerServices()
    {
        DisposalProbe? probe = null;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InMemoryMcpClientServerHarness.CreateAsync(
                transportName: "cancelled-client-initialization",
                clientCapabilities: new ClientCapabilities(),
                clientHandlers: new McpClientHandlers(),
                disposalFailureContext: "cancelled-client-initialization",
                cancellationToken: cancellation.Token,
                serverServicesFactory: () =>
                {
                    var provider = new ServiceCollection()
                        .AddSingleton<DisposalProbe>()
                        .BuildServiceProvider();
                    probe = provider.GetRequiredService<DisposalProbe>();
                    return provider;
                }));

        Assert.IsNotNull(probe);
        Assert.IsTrue(
            probe.DisposeAsyncCalled,
            "A failed client handshake must dispose the server service provider it created.");
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task CreateAsync_WhenHostedRootsClientInitializationIsCancelled_StopsAndDisposesHost()
    {
        using var clientCancellation = new CancellationTokenSource();
        var probe = new DisposalProbe();
        CancelAfterStartHost? ownedHost = null;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            InMemoryMcpClientServerHarness.CreateAsync(
                new InMemoryMcpHarnessOptions(
                    TransportName: "cancelled-hosted-roots-client-initialization",
                    ClientCapabilities: new ClientCapabilities(),
                    ClientHandlers: new McpClientHandlers(),
                    DisposalFailureContext: "cancelled-hosted-roots-client-initialization")
                {
                    ProtocolVersion = null,
                    ServerHostFactory = (serverInput, serverOutput) =>
                    {
                        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
                        builder.Logging.ClearProviders();
                        builder.Services.AddSingleton(_ => probe);
                        builder.Services.AddSingleton(new SecurityOptions { SanctionedRoots = [Path.GetTempPath()] });
                        builder.Services
                            .AddMcpServer()
                            .WithTools<RoslynMcp.Tests.McpRootsProbeTools>()
                            .WithStreamServerTransport(serverInput, serverOutput);
                        ownedHost = new CancelAfterStartHost(builder.Build(), clientCancellation);
                        _ = ownedHost.Services.GetRequiredService<DisposalProbe>();
                        return ownedHost;
                    },
                },
                clientCancellation.Token));

        Assert.IsNotNull(ownedHost);
        Assert.IsTrue(ownedHost.StopCalled, "The hosted server must stop before failed client teardown completes.");
        Assert.IsTrue(ownedHost.DisposeCalled, "The hosted server must be disposed after client initialization fails.");
        Assert.IsTrue(probe.DisposeAsyncCalled, "Host-owned services must be disposed on initialization failure.");
    }

    private sealed class DisposalProbe : IAsyncDisposable
    {
        public bool DisposeAsyncCalled { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelAfterStartHost(IHost inner, CancellationTokenSource clientCancellation) : IHost
    {
        public bool StopCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public IServiceProvider Services => inner.Services;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await inner.StartAsync(cancellationToken);
            clientCancellation.Cancel();
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            return inner.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            DisposeCalled = true;
            inner.Dispose();
        }
    }
}
