using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class FixtureCleanupScopeTests
{
    [TestMethod]
    public void Run_PreservesPrimaryFailureWhenCleanupSucceeds()
    {
        var primary = new InvalidOperationException("body failed");
        var cleanupRan = false;

        var observed = Assert.ThrowsExactly<InvalidOperationException>(() =>
            FixtureCleanupScope.Run(() => throw primary, () => cleanupRan = true));

        Assert.AreSame(primary, observed);
        Assert.IsTrue(cleanupRan);
        StringAssert.Contains(observed.StackTrace!, nameof(Run_PreservesPrimaryFailureWhenCleanupSucceeds));
    }

    [TestMethod]
    public async Task RunAsync_PreservesPrimaryFailureWhenCleanupSucceeds()
    {
        var primary = new InvalidOperationException("body failed");
        var cleanupRan = false;

        async Task FailAsync()
        {
            await Task.Yield();
            throw primary;
        }

        var observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            FixtureCleanupScope.RunAsync(FailAsync, () => cleanupRan = true));

        Assert.AreSame(primary, observed);
        Assert.IsTrue(cleanupRan);
        StringAssert.Contains(observed.StackTrace!, nameof(FailAsync));
    }

    [TestMethod]
    public async Task RunAsync_DynamicCleanupSteps_PreservesBodyAndEveryCleanupFailure()
    {
        var primary = new InvalidOperationException("body failed");
        var firstCleanup = new IOException("first cleanup failed");
        var secondCleanup = new IOException("second cleanup failed");
        List<Func<ValueTask>> cleanupSteps =
        [
            CleanupFailureCollector.FromAction(() => throw firstCleanup)
        ];

        var observed = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            FixtureCleanupScope.RunAsync(
                () =>
                {
                    cleanupSteps.Add(CleanupFailureCollector.FromAction(() => throw secondCleanup));
                    throw primary;
                },
                cleanupSteps));

        CollectionAssert.AreEqual(
            new Exception[] { primary, firstCleanup, secondCleanup },
            observed.InnerExceptions.ToArray());
    }
}
