using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HostShutdownLifecycleTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HostShutdown_CompletesInFlightWorkBeforeSingleContainerOwnedDisposal(
        bool cancelRun)
    {
        var request = new InFlightRequest();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(request);
        builder.Services.AddSingleton<DisposalSentinel>();
        builder.Services.AddHostedService<InFlightRequestService>();
        var host = builder.Build();
        var sentinel = host.Services.GetRequiredService<DisposalSentinel>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStopping.Register(stopping.SetResult);
        using var cancellation = new CancellationTokenSource();

        var run = host.RunAsync(cancellation.Token);
        await request.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        if (cancelRun)
        {
            cancellation.Cancel();
        }
        else
        {
            lifetime.StopApplication();
        }

        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, sentinel.DisposeCount,
            "ApplicationStopping must not dispose DI-owned singletons while work is in flight.");

        request.Complete.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, sentinel.DisposeCount,
            "The host container must own exactly one singleton teardown.");
        Assert.IsTrue(request.Completed,
            "The in-flight operation must finish before container-owned teardown.");
    }

    private sealed class InFlightRequest
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; set; }
    }

    private sealed class InFlightRequestService(InFlightRequest request) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            request.Started.SetResult();
            await request.Complete.Task.ConfigureAwait(false);
            request.Completed = true;
        }
    }

    private sealed class DisposalSentinel(InFlightRequest request) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            Assert.IsTrue(request.Completed,
                "Container disposal must occur after the in-flight operation completes.");
            DisposeCount++;
        }
    }
}
