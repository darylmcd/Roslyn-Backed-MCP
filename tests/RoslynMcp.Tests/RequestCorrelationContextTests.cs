using System.Collections.Concurrent;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class RequestCorrelationContextTests
{
    [TestMethod]
    public async Task IncomingFilter_IsolatesConcurrentAndLaterRequests()
    {
        var identifiers = new ConcurrentQueue<string>();
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;

        McpMessageHandler filtered = RequestCorrelationMessageFilter.Create(async (_, _) =>
        {
            identifiers.Enqueue(RequestCorrelationContext.Current
                ?? throw new AssertFailedException("Dispatch must have a correlation identifier."));
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.SetResult();
            }

            await release.Task.ConfigureAwait(false);
        });

        var first = filtered(null!, CancellationToken.None);
        var second = filtered(null!, CancellationToken.None);
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.HasCount(2, identifiers);
        var concurrent = identifiers.ToArray();
        Assert.AreNotEqual(concurrent[0], concurrent[1]);
        Assert.IsNull(RequestCorrelationContext.Current);

        string? laterIdentifier = null;
        var later = RequestCorrelationMessageFilter.Create((_, _) =>
        {
            laterIdentifier = RequestCorrelationContext.Current;
            return Task.CompletedTask;
        });
        await later(null!, CancellationToken.None);

        Assert.IsNotNull(laterIdentifier);
        Assert.IsFalse(concurrent.Contains(laterIdentifier, StringComparer.Ordinal));
        Assert.IsNull(RequestCorrelationContext.Current);
    }

    [TestMethod]
    public async Task IncomingFilter_ClearsContextAfterFailureAndCancellation()
    {
        var failing = RequestCorrelationMessageFilter.Create((_, _) =>
            throw new InvalidOperationException("synthetic failure"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => failing(null!, CancellationToken.None));
        Assert.IsNull(RequestCorrelationContext.Current);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = RequestCorrelationMessageFilter.Create((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => cancelled(null!, cancellation.Token));
        Assert.IsNull(RequestCorrelationContext.Current);
    }
}
