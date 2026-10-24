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
        using var stoppingRegistration = lifetime.ApplicationStopping.Register(stopping.SetResult);
        using var cancellation = new CancellationTokenSource();

        var run = host.RunAsync(cancellation.Token);
        try
        {
            Assert.IsTrue(request.Started.Task.IsCompletedSuccessfully,
                "The cooperative fixture must establish in-flight work during host startup.");

            if (cancelRun)
            {
                cancellation.Cancel();
            }
            else
            {
                lifetime.StopApplication();
            }

            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await request.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, sentinel.DisposeCount,
                "ApplicationStopping must not dispose DI-owned singletons while work is in flight.");
            Assert.IsFalse(run.IsCompleted,
                "RunAsync must await the hosted service's in-flight work before disposing the container.");

            request.Complete.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(1, sentinel.DisposeCount,
                "The host container must own exactly one singleton teardown.");
            Assert.IsTrue(request.Completed,
                "The in-flight operation must finish before container-owned teardown.");
        }
        finally
        {
            request.Complete.TrySetResult();
            if (!run.IsCompleted)
                lifetime.StopApplication();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class InFlightRequest
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; set; }
    }

    private sealed class InFlightRequestService(InFlightRequest request) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            request.Started.SetResult();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            request.StopEntered.SetResult();
            await request.Complete.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
