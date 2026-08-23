using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CleanupFailureCollectorTests
{
    [TestMethod]
    public async Task RunAsync_DisposalFailureRemainsObservableAndLaterStepsStillRun()
    {
        var laterStepRan = false;
        var disposalFailure = new InvalidOperationException("injected workspace disposal failure");

        var aggregate = await Assert.ThrowsExactlyAsync<AggregateException>(async () =>
            await CleanupFailureCollector.RunAsync(
                "cleanup failed",
                CleanupFailureCollector.FromAction(() => throw disposalFailure),
                CleanupFailureCollector.FromAction(() => laterStepRan = true)));

        Assert.IsTrue(laterStepRan, "A failed disposal must not prevent later cleanup steps.");
        Assert.HasCount(1, aggregate.InnerExceptions);
        Assert.AreSame(disposalFailure, aggregate.InnerExceptions[0]);
    }
}
