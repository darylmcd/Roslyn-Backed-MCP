namespace RoslynMcp.Tests.Helpers;

internal static class TimeBoundTestOperation
{
    private static readonly TimeSpan SettlementGrace = TimeSpan.FromSeconds(10);

    internal static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource();
        var task = operation(cancellation.Token);
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException) when (!task.IsCompleted)
        {
            var settlementFaults = new List<Exception>();
            try
            {
                cancellation.Cancel();
            }
            catch (Exception exception)
            {
                settlementFaults.Add(exception);
            }

            try
            {
                await task.WaitAsync(SettlementGrace);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The operation has settled in response to test-owned cancellation.
            }
            catch (TimeoutException) when (!task.IsCompleted)
            {
                // Returning would let the still-running task overlap shared fixture cleanup.
                Environment.FailFast($"A timed-out test operation ignored cancellation for {SettlementGrace}.");
                throw;
            }
            catch (Exception exception)
            {
                settlementFaults.Add(exception);
            }

            var cause = settlementFaults.Count switch
            {
                0 => null,
                1 => settlementFaults[0],
                _ => new AggregateException(settlementFaults)
            };
            throw new TimeoutException($"The test operation exceeded its {timeout} limit.", cause);
        }
    }
}
