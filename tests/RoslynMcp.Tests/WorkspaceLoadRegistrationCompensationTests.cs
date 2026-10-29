using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>workspace-load-registration-compensation</c>: when
/// <see cref="WorkspaceManager.LoadAsync(string, CancellationToken)"/> fails after the session was added to the
/// registry (file-watcher registration or status construction throws), the exact session must be removed
/// through the normal close path so the capacity slot returns, the watcher is unwatched and the
/// primary failure is preserved.
/// </summary>
// Same isolation rationale as WorkspaceSessionLoaderFailureTests: each test builds a private WorkspaceManager with
// its own PreviewStore and watcher double, touches no TestBase state and no static; the only shared resource is the
// checked-in samples/SampleSolution obj/ artifacts written by the load's design-time build.
[TestClass]
public sealed class WorkspaceLoadRegistrationCompensationTests
{
    private static string _sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        var repoRoot = TestFixtureFileSystem.FindRepositoryRoot();
        _sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            repoRoot,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    private static WorkspaceManager CreateManager(IFileWatcherService watcher, int cap) =>
        new(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = cap },
            cacheStore: null,
            sessionLoader: null,
            ownsFileWatcher: false);

    [TestMethod]
    public async Task LoadAsync_WatchThrows_RemovesExactSession_ReturnsSlot_AndNextLoadSucceeds()
    {
        using var watcher = new ScriptedWatcher();
        using var manager = CreateManager(watcher, cap: 1);
        var closed = new List<string>();
        manager.WorkspaceClosed += closed.Add;
        watcher.ThrowOnWatchCalls = 1;

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.LoadAsync(_sampleSolutionPath, CancellationToken.None));
        StringAssert.Contains(thrown.Message, ScriptedWatcher.WatchFailureMessage);

        var failedId = watcher.WatchedIds.Single();
        Assert.AreEqual(0, manager.ListWorkspaces().Count, "The failed session must not remain registered.");
        Assert.IsFalse(manager.ContainsWorkspace(failedId));
        CollectionAssert.AreEqual(new[] { failedId }, watcher.UnwatchedIds, "The failed id must be unwatched exactly once.");
        CollectionAssert.AreEqual(new[] { failedId }, closed, "WorkspaceClosed must fire once for the failed id.");

        // Cap is 1: succeeds only if the failed load returned its slot and left no zombie to dedup against.
        var status = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        Assert.IsTrue(status.IsLoaded);
        Assert.AreNotEqual(failedId, status.WorkspaceId);
        Assert.AreEqual(1, manager.ListWorkspaces().Count);
    }

    [TestMethod]
    public async Task LoadAsync_StatusConstructionThrows_RemovesExactSession_AndReturnsSlot()
    {
        using var watcher = new ScriptedWatcher { ThrowOnIsStaleCalls = 1 };
        using var manager = CreateManager(watcher, cap: 1);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.LoadAsync(_sampleSolutionPath, CancellationToken.None));

        Assert.AreEqual(0, manager.ListWorkspaces().Count);
        var status = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        Assert.IsTrue(status.IsLoaded);
    }

    [TestMethod]
    public async Task LoadAsync_WatchThrows_LeavesUnrelatedSessionUntouched()
    {
        using var watcher = new ScriptedWatcher();
        using var manager = CreateManager(watcher, cap: 2);

        var healthy = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        watcher.ThrowOnWatchCalls = 1;
        // Global properties bypass path dedup so the second load really creates a new session.
        var globals = new Dictionary<string, string> { ["Configuration"] = "Release" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.LoadAsync(_sampleSolutionPath, globals, CancellationToken.None));

        Assert.IsTrue(manager.ContainsWorkspace(healthy.WorkspaceId));
        Assert.IsNotNull(manager.GetCurrentSolution(healthy.WorkspaceId));
        CollectionAssert.DoesNotContain(watcher.UnwatchedIds, healthy.WorkspaceId);
        Assert.AreEqual(1, manager.ListWorkspaces().Count);

        // Cap 2: the failed load returned its slot, so another session still fits next to the healthy one.
        var third = await manager.LoadAsync(_sampleSolutionPath, globals, CancellationToken.None);
        Assert.IsTrue(third.IsLoaded);
        Assert.AreEqual(2, manager.ListWorkspaces().Count);
    }

    [TestMethod]
    public async Task LoadAsync_WatchAndUnwatchBothThrow_PreservesPrimaryFailure_AndReturnsSlot()
    {
        using var watcher = new ScriptedWatcher { ThrowOnUnwatch = true };
        using var manager = CreateManager(watcher, cap: 1);
        watcher.ThrowOnWatchCalls = 1;

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.LoadAsync(_sampleSolutionPath, CancellationToken.None));
        StringAssert.Contains(thrown.Message, ScriptedWatcher.WatchFailureMessage);

        Assert.AreEqual(0, manager.ListWorkspaces().Count);
        watcher.ThrowOnUnwatch = false;
        var status = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        Assert.IsTrue(status.IsLoaded, "Slot must be returned even when the cleanup itself fails.");
    }

    /// <summary>Delegates to a real watcher; throws on demand from Watch / IsStale / Unwatch.</summary>
    private sealed class ScriptedWatcher : IFileWatcherService
    {
        public const string WatchFailureMessage = "Simulated watcher registration failure";

        private readonly FileWatcherService _inner = new(NullLogger<FileWatcherService>.Instance);

        public int ThrowOnWatchCalls { get; set; }
        public int ThrowOnIsStaleCalls { get; set; }
        public bool ThrowOnUnwatch { get; set; }
        public List<string> WatchedIds { get; } = [];
        public List<string> UnwatchedIds { get; } = [];

        public void Watch(string workspaceId, string workspacePath)
        {
            WatchedIds.Add(workspaceId);
            if (ThrowOnWatchCalls > 0)
            {
                ThrowOnWatchCalls--;
                throw new InvalidOperationException(WatchFailureMessage);
            }
            _inner.Watch(workspaceId, workspacePath);
        }

        public void Unwatch(string workspaceId)
        {
            UnwatchedIds.Add(workspaceId);
            if (ThrowOnUnwatch) throw new InvalidOperationException("Simulated unwatch failure");
            _inner.Unwatch(workspaceId);
        }

        public bool IsStale(string workspaceId)
        {
            if (ThrowOnIsStaleCalls > 0)
            {
                ThrowOnIsStaleCalls--;
                throw new InvalidOperationException("Simulated IsStale failure");
            }
            return _inner.IsStale(workspaceId);
        }

        public Task WaitForStaleAsync(string workspaceId, CancellationToken ct) => _inner.WaitForStaleAsync(workspaceId, ct);
        public string? GetStaleReason(string workspaceId) => _inner.GetStaleReason(workspaceId);
        public void MarkStale(string workspaceId, string reason) => _inner.MarkStale(workspaceId, reason);
        public void ClearStale(string workspaceId) => _inner.ClearStale(workspaceId);
        public void Dispose() => _inner.Dispose();
    }
}
