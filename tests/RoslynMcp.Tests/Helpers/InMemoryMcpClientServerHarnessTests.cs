using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RoslynMcp.Tests.Helpers;

[TestClass]
public sealed class InMemoryMcpClientServerHarnessTests
{
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

    private sealed class DisposalProbe : IAsyncDisposable
    {
        public bool DisposeAsyncCalled { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
