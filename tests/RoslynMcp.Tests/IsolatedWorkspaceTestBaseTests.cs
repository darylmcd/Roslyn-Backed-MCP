namespace RoslynMcp.Tests;

[TestClass]
public sealed class IsolatedWorkspaceTestBaseTests
{
    [TestMethod]
    public async Task InitializeWithCleanupAsync_WhenInitializationFails_DisposesAndPreservesFailure()
    {
        var initializationFailure = new FixtureException("initialization failed");
        var resource = new FixtureResource();

        var actual = await Assert.ThrowsExactlyAsync<FixtureException>(() =>
            IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(
                resource,
                (_, _) => Task.FromException(initializationFailure)));

        Assert.AreSame(initializationFailure, actual);
        Assert.IsTrue(resource.IsDisposed);
    }

    [TestMethod]
    public async Task InitializeWithCleanupAsync_WhenInitializationAndCleanupFail_AggregatesBoth()
    {
        var initializationFailure = new FixtureException("initialization failed");
        var cleanupFailure = new FixtureException("cleanup failed");
        var resource = new FixtureResource(cleanupFailure);

        var actual = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(
                resource,
                (_, _) => Task.FromException(initializationFailure)));

        CollectionAssert.AreEqual(
            new Exception[] { initializationFailure, cleanupFailure },
            actual.InnerExceptions.ToArray());
        Assert.IsTrue(resource.IsDisposed);
    }

    private sealed class FixtureResource(Exception? cleanupFailure = null) : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return cleanupFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(cleanupFailure);
        }
    }

    private sealed class FixtureException(string message) : Exception(message);
}
