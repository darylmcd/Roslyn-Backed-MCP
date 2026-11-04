using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TimeBoundTestOperationTests
{
    [TestMethod]
    public async Task Timeout_PreservesPrimaryFailureWhenSettlementFaults()
    {
        var timeout = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            TimeBoundTestOperation.RunAsync(async token =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException("settlement failed");
                }

                return 1;
            }, TimeSpan.FromMilliseconds(50)));

        Assert.IsInstanceOfType<InvalidOperationException>(timeout.InnerException);
        Assert.AreEqual("settlement failed", timeout.InnerException.Message);
    }

    [TestMethod]
    public async Task Timeout_DrainsOperationWhenCancellationCallbackFaults()
    {
        var settled = false;
        var timeout = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            TimeBoundTestOperation.RunAsync(async token =>
            {
                using var registration = token.Register(() => throw new InvalidOperationException("callback failed"));
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    settled = true;
                }

                return 1;
            }, TimeSpan.FromMilliseconds(50)));

        Assert.IsTrue(settled, "A failing cancellation callback must not skip operation settlement.");
        var callbackFailure = Assert.IsInstanceOfType<AggregateException>(timeout.InnerException);
        Assert.IsTrue(callbackFailure.InnerExceptions.Any(e =>
            e is InvalidOperationException { Message: "callback failed" }));
    }

    [TestMethod]
    public async Task Timeout_CancelsAndSettlesOperationBeforeReturning()
    {
        using var cleanup = new CancellationTokenSource();
        var settled = false;
        try
        {
            await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
                await TimeBoundTestOperation.RunAsync(async token =>
                {
                    try
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
                        await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                    }
                    finally
                    {
                        settled = true;
                    }

                    return 1;
                }, TimeSpan.FromMilliseconds(50)));

            Assert.IsTrue(settled, "The timed-out operation must settle before its test fixture can be cleaned up.");
        }
        finally
        {
            cleanup.Cancel();
        }
    }
}
