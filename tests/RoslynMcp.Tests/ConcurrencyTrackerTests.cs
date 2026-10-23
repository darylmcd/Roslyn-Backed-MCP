using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConcurrencyTrackerTests
{
    [TestMethod]
    public async Task ConcurrentEntries_RecordEveryActiveCaller_AndRetainThePeak()
    {
        var tracker = new ConcurrencyTracker();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Exception? resourcePrimary = null;
        try
        {
            List<Task> callers = [];
            Exception? callerPrimary = null;
            try
            {
                foreach (var _ in Enumerable.Range(0, 32))
                {
                    callers.Add(Task.Run(async () =>
                    {
                        tracker.Enter();
                        try
                        {
                            if (Interlocked.Increment(ref count) == 32) entered.SetResult();
                            await release.Task.WaitAsync(deadline.Token);
                        }
                        finally { tracker.Leave(); }
                    }));
                }
                await entered.Task.WaitAsync(deadline.Token);
                Assert.AreEqual(32, tracker.MaxConcurrent);
            }
            catch (Exception failure) { callerPrimary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Competing tracker callers failed to drain.", callerPrimary,
                    CleanupFailureCollector.FromAction(() => { release.TrySetResult(); }),
                    async () => await Task.WhenAll(callers));
            }

            tracker.Enter();
            tracker.Leave();
            Assert.AreEqual(32, tracker.MaxConcurrent);
            Assert.ThrowsExactly<InvalidOperationException>(tracker.Leave);
        }
        catch (Exception failure) { resourcePrimary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Tracker deadline cleanup failed.", resourcePrimary, deadline); }
    }
}
