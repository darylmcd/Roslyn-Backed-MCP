using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputCaptureBatchTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task InvalidCapacity_PerformsNoCapture(int capacity)
    {
        var calls = 0;
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => PhysicalInputCaptureBatch.RunAsync(ImmutableArray.Create("owned"), (path, ct) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new PhysicalInputIdentity(path, false, []));
        }, capacity, CancellationToken.None));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(4)]
    public async Task Window_IsBoundedAndDrainedBeforeNextWindow_AndReturnsInputOrder(int capacity)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var input = Enumerable.Range(0, 9).ToImmutableArray();
        var operation = PhysicalInputCaptureBatch.RunAsync(input, async (index, ct) =>
        {
            if (Interlocked.Increment(ref calls) == capacity) started.SetResult();
            if (index < capacity) await release.Task.WaitAsync(ct);
            return new PhysicalInputIdentity(index.ToString(), false, []);
        }, capacity, CancellationToken.None);
        Exception? primary = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(capacity, Volatile.Read(ref calls));
            Assert.IsFalse(operation.IsCompleted);
        }
        catch (Exception primaryFailure) { primary = primaryFailure; throw; }
        finally { release.TrySetResult(); if (primary is not null) await CleanupFailureCollector.RunAfterFailureAsync("Batch drain failed.", primary, async () => { await operation; }); }
        var result = await operation;
        CollectionAssert.AreEqual(input.Select(index => index.ToString()).ToArray(), result.Select(item => item.FilePath).ToArray());
    }

    [TestMethod]
    public async Task LaterIoFailure_CannotOvertakeEarlierMismatch_AndAllWorkersDrain()
    {
        var laterFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var completed = 0;
        var operation = PhysicalInputCaptureBatch.RunAsync(Enumerable.Range(0, 8).ToImmutableArray(), async (index, ct) =>
        {
            Interlocked.Increment(ref calls);
            try
            {
                if (index == 1) { laterFailed.SetResult(); throw new IOException("later read"); }
                await release.Task.WaitAsync(ct);
                if (index == 0) throw new PublicInvalidOperationException("earlier mismatch");
                return new PhysicalInputIdentity(index.ToString(), false, []);
            }
            finally { Interlocked.Increment(ref completed); }
        }, 4, CancellationToken.None);
        Exception? primary = null;
        try
        {
            await laterFailed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(operation.IsCompleted, "A failed task cannot leave other owned captures running.");
        }
        catch (Exception primaryFailure) { primary = primaryFailure; throw; }
        finally { release.TrySetResult(); if (primary is not null) await CleanupFailureCollector.RunAfterFailureAsync("Batch drain failed.", primary, async () => { await operation; }); }
        var failure = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => operation);
        Assert.AreEqual("earlier mismatch", failure.Message);
        Assert.AreEqual(4, calls);
        Assert.AreEqual(calls, completed);
    }

    [TestMethod]
    public async Task Cancellation_DrainsStartedWindow_AndDoesNotDispatchAnother()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var completed = 0;
        var operation = PhysicalInputCaptureBatch.RunAsync(Enumerable.Range(0, 8).ToImmutableArray(), async (index, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 4) started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { Interlocked.Increment(ref completed); }
            return new PhysicalInputIdentity(index.ToString(), false, []);
        }, 4, cancellation.Token);
        Exception? primary = null;
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception primaryFailure) { primary = primaryFailure; throw; }
        finally { cancellation.Cancel(); if (primary is not null) await CleanupFailureCollector.RunAfterFailureAsync("Batch drain failed.", primary, async () => { await operation; }); }
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(4, calls);
        Assert.AreEqual(calls, completed);
    }

    [TestMethod]
    public async Task PreCancelledBatch_PerformsNoCapture()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => PhysicalInputCaptureBatch.RunAsync(ImmutableArray.Create("owned"), (path, ct) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new PhysicalInputIdentity(path, false, []));
        }, 4, cancellation.Token));
        Assert.AreEqual(0, calls);
    }
}
