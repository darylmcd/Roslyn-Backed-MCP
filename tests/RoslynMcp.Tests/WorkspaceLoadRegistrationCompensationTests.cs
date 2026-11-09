using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
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

    private static WorkspaceManager CreateManager(
        IFileWatcherService watcher,
        int cap,
        ILogger<WorkspaceManager>? logger = null,
        WorkspaceSessionLoader? loader = null) =>
        new(
            logger ?? NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = cap },
            cacheStore: null,
            sessionLoader: loader,
            ownsFileWatcher: false);

    [TestMethod]
    public async Task LoadAsync_ShutdownWaitsForUncooperativeLoad_WithoutPublishingSession()
    {
        using var watcher = new ScriptedWatcher();
        var loader = new CompletedLoadGate();
        var manager = CreateManager(watcher, cap: 1, loader: loader);
        var load = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        await loader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(120));

        try
        {
            var shutdown = manager.DisposeAsync().AsTask();
            Assert.IsFalse(shutdown.IsCompleted, "Shutdown must drain the admitted load before disposing its slot and watcher.");
            loader.Release.SetResult();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => load.WaitAsync(TimeSpan.FromSeconds(120)));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(120));
            Assert.AreEqual(0, watcher.WatchedIds.Count);
            Assert.AreEqual(0, manager.ListWorkspaces().Count);
        }
        finally
        {
            loader.Release.TrySetResult();
            try { await manager.DisposeAsync(); } catch { /* Preserve the primary assertion failure. */ }
            try { await load; } catch { /* Observe the load after the gate is released. */ }
        }
    }

    [TestMethod]
    public async Task LoadAsync_CloseCannotOvertakeWatcherRegistration()
    {
        using var watcher = new ScriptedWatcher { PauseWatch = true };
        using var manager = CreateManager(watcher, cap: 1);
        var load = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        await watcher.WatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(120));

        try
        {
            var workspaceId = watcher.WatchedIds.Single();
            var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var close = Task.Run(() =>
            {
                closeStarted.SetResult();
                return manager.Close(workspaceId);
            });
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(100);
            Assert.IsFalse(close.IsCompleted, "Close must wait for watcher registration to finish.");
            watcher.ReleaseWatch.SetResult();
            await load.WaitAsync(TimeSpan.FromSeconds(120));
            Assert.IsTrue(await close.WaitAsync(TimeSpan.FromSeconds(30)));
            CollectionAssert.AreEqual(new[] { workspaceId }, watcher.UnwatchedIds);
            Assert.AreEqual(0, manager.ListWorkspaces().Count);

            var replacement = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
            Assert.IsTrue(replacement.IsLoaded, "Close must return the slot exactly once.");
        }
        finally
        {
            watcher.ReleaseWatch.TrySetResult();
            try { await load; } catch { /* Observe the load after releasing the watcher. */ }
        }
    }

    [TestMethod]
    public async Task LoadAsync_TwoLoadsReadyToPublish_ReturnOneSession()
    {
        using var watcher = new ScriptedWatcher();
        using var manager = CreateManager(watcher, cap: 2);
        using var ready = new Barrier(2);
        manager.BeforeLoadPublicationForTesting = () =>
            Assert.IsTrue(ready.SignalAndWait(TimeSpan.FromSeconds(120)));
        var first = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        var second = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);

        var statusA = await first.WaitAsync(TimeSpan.FromSeconds(120));
        var statusB = await second.WaitAsync(TimeSpan.FromSeconds(120));
        Assert.AreEqual(statusA.WorkspaceId, statusB.WorkspaceId);
        Assert.AreEqual(1, manager.ListWorkspaces().Count);
        Assert.AreEqual(1, watcher.WatchedIds.Count);
    }

    private sealed class CompletedLoadGate : WorkspaceSessionLoader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId, string path, IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics, ILogger logger, CancellationToken ct)
        {
            var loaded = await base.CreateAndOpenAsync(workspaceId, path, globalProperties, diagnostics, logger, ct).ConfigureAwait(false);
            Entered.SetResult();
            await Release.Task.ConfigureAwait(false);
            return loaded;
        }
    }

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
        var logger = new ListLogger<WorkspaceManager>();
        using var manager = CreateManager(watcher, cap: 1, logger);
        watcher.ThrowOnWatchCalls = 1;

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.LoadAsync(_sampleSolutionPath, CancellationToken.None));
        StringAssert.Contains(thrown.Message, ScriptedWatcher.WatchFailureMessage);

        Assert.AreEqual(0, manager.ListWorkspaces().Count);
        var cleanupLog = logger.Entries.Single(e => e.Level == LogLevel.Error && e.Exception is AggregateException);
        StringAssert.Contains(cleanupLog.Message, "Cleanup after failed workspace load");
        StringAssert.Contains(cleanupLog.Exception!.ToString(), "Simulated unwatch failure");
        watcher.ThrowOnUnwatch = false;
        var status = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        Assert.IsTrue(status.IsLoaded, "Slot must be returned even when the cleanup itself fails.");
    }

    [TestMethod]
    public async Task LoadAsync_RaceLoserDisposeThrows_StillReturnsWinnerStatus_AndLogsFailure()
    {
        using var watcher = new ScriptedWatcher();
        var logger = new ListLogger<WorkspaceManager>();
        var loader = new GatedLoader();
        using var manager = CreateManager(watcher, cap: 2, logger, loader);

        // Both loads pass the pre-load dedup check and block inside the loader; A is then released
        // first and registers, so B deterministically loses the post-load race check.
        var loadA = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        await loader.Entered[0].Task.WaitAsync(TimeSpan.FromSeconds(60));
        var loadB = manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        await loader.Entered[1].Task.WaitAsync(TimeSpan.FromSeconds(60));

        loader.Release[0].SetResult();
        var statusA = await loadA.WaitAsync(TimeSpan.FromSeconds(120));
        loader.Release[1].SetResult();
        var statusB = await loadB.WaitAsync(TimeSpan.FromSeconds(120));

        Assert.AreEqual(statusA.WorkspaceId, statusB.WorkspaceId, "The loser must return the winner's status.");
        Assert.AreEqual(1, manager.ListWorkspaces().Count);
        var log = logger.Entries.Single(e => e.Level == LogLevel.Error && e.Exception is AggregateException);
        StringAssert.Contains(log.Message, "race-losing workspace");
        Assert.IsInstanceOfType<NullReferenceException>(((AggregateException)log.Exception!).InnerExceptions.Single());

        // Loser's slot was returned: cap 2 still admits another (distinct) session.
        var third = await manager.LoadAsync(
            _sampleSolutionPath,
            new Dictionary<string, string> { ["Configuration"] = "Release" },
            CancellationToken.None);
        Assert.IsTrue(third.IsLoaded);
        Assert.AreEqual(2, manager.ListWorkspaces().Count);
    }

    [TestMethod]
    public async Task ReloadAsync_PreviewInvalidationThrows_DisposesRetiredNotLiveWorkspace()
    {
        using var watcher = new ScriptedWatcher();
        var previews = new ThrowingInvalidationPreviewStore(new PreviewStore());
        using var manager = new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            previews,
            watcher,
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 1 },
            cacheStore: null,
            sessionLoader: null,
            ownsFileWatcher: false);
        var reloaded = new List<string>();
        manager.WorkspaceReloaded += reloaded.Add;

        var status = await manager.LoadAsync(_sampleSolutionPath, CancellationToken.None);
        var id = status.WorkspaceId;
        var oldWorkspace = manager.GetCurrentSolution(id).Workspace;
        Assert.IsTrue(oldWorkspace.CurrentSolution.Projects.Any(), "Precondition: the loaded workspace is populated.");
        // The reload must go through ClearStale only on success; mark stale so the assertion below can see it survive.
        watcher.MarkStale(id, StaleReasons.ExternalEdit);

        previews.ThrowOnInvalidate = true;
        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.ReloadAsync(id, CancellationToken.None));
        StringAssert.Contains(thrown.Message, ThrowingInvalidationPreviewStore.FailureMessage);

        // The swap committed: the session serves the NEW workspace and it is not disposed.
        var live = manager.GetCurrentSolution(id);
        Assert.AreNotSame(oldWorkspace, live.Workspace, "Session must point at the new workspace.");
        Assert.IsTrue(live.Projects.Any(), "The live workspace must not have been disposed by the failure path.");
        // The retired workspace was disposed (a disposed MSBuildWorkspace drops its solution).
        Assert.IsFalse(oldWorkspace.CurrentSolution.Projects.Any(), "The retired workspace must be disposed.");
        // Pinned decision: the committed swap still announces the reload so event-driven caches drop the old solution.
        CollectionAssert.AreEqual(new[] { id }, reloaded);
        // Pinned decision: ClearStale is skipped on failure, so the workspace stays stale and the next gated call reloads.
        Assert.IsTrue(watcher.IsStale(id), "A failed reload must not clear the stale flag.");

        previews.ThrowOnInvalidate = false;
        var recovered = await manager.ReloadAsync(id, CancellationToken.None);
        Assert.IsTrue(recovered.IsLoaded);
        Assert.IsFalse(watcher.IsStale(id));
        Assert.IsTrue(manager.GetCurrentSolution(id).Projects.Any());
        CollectionAssert.AreEqual(new[] { id, id }, reloaded);
    }

    /// <summary>Delegates to a real store; throws from InvalidateOnVersionBump on demand.</summary>
    private sealed class ThrowingInvalidationPreviewStore(IPreviewStore inner) : IPreviewStore
    {
        public const string FailureMessage = "Simulated preview invalidation failure";

        public bool ThrowOnInvalidate { get; set; }

        public void InvalidateOnVersionBump(string workspaceId, int newWorkspaceVersion)
        {
            if (ThrowOnInvalidate) throw new InvalidOperationException(FailureMessage);
            inner.InvalidateOnVersionBump(workspaceId, newWorkspaceVersion);
        }

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description) =>
            inner.Store(workspaceId, modifiedSolution, workspaceVersion, description);

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated) =>
            inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated);

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, bool diffTruncated, PreviewKind kind) =>
            inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, diffTruncated, kind);

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes) =>
            inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes);

        public string Store(string workspaceId, Solution modifiedSolution, int workspaceVersion, string description, IReadOnlyList<FileChangeDto> changes, PreviewKind kind) =>
            inner.Store(workspaceId, modifiedSolution, workspaceVersion, description, changes, kind);

        public (string WorkspaceId, Solution OriginalSolution, Solution ModifiedSolution, int WorkspaceVersion, string Description, bool DiffTruncated)? Retrieve(string token) => inner.Retrieve(token);
        public void Invalidate(string token) => inner.Invalidate(token);
        public void InvalidateAll(string? workspaceId = null) => inner.InvalidateAll(workspaceId);
        public string? PeekWorkspaceId(string token) => inner.PeekWorkspaceId(token);
        public IReadOnlyList<string>? PeekChangedPaths(string token) => inner.PeekChangedPaths(token);
        public PreviewKind PeekKind(string token) => inner.PeekKind(token);
    }

    /// <summary>
    /// Real loads gated per call so two concurrent loads can be ordered. The second call's lease throws
    /// on Dispose (null loader element: NullReferenceException is deterministic; the real loader type is sealed).
    /// </summary>
    private sealed class GatedLoader : WorkspaceSessionLoader
    {
        private int _calls = -1;

        public TaskCompletionSource[] Entered { get; } = [NewGate(), NewGate()];
        public TaskCompletionSource[] Release { get; } = [NewGate(), NewGate()];

        private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
            string workspaceId,
            string path,
            IDictionary<string, string>? globalProperties,
            WorkspaceDiagnosticsSink diagnostics,
            ILogger logger,
            CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call > 1)
                return await base.CreateAndOpenAsync(workspaceId, path, globalProperties, diagnostics, logger, ct).ConfigureAwait(false);

            Entered[call].SetResult();
            await Release[call].Task.ConfigureAwait(false);
            var (workspace, lease) = await base.CreateAndOpenAsync(
                workspaceId, path, globalProperties, diagnostics, logger, ct).ConfigureAwait(false);
            if (call == 0) return (workspace, lease);

            lease.Dispose();
            return (workspace, new AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease(
                null, null, new AnalyzerReferenceIsolation.ShadowCopyAnalyzerAssemblyLoader[] { null! }, 0, null));
        }
    }

    /// <summary>Delegates to a real watcher; throws on demand from Watch / IsStale / Unwatch.</summary>
    private sealed class ScriptedWatcher : IFileWatcherService
    {
        public const string WatchFailureMessage = "Simulated watcher registration failure";

        private readonly FileWatcherService _inner = new(NullLogger<FileWatcherService>.Instance);

        public int ThrowOnWatchCalls { get; set; }
        public int ThrowOnIsStaleCalls { get; set; }
        public bool ThrowOnUnwatch { get; set; }
        public bool PauseWatch { get; set; }
        public TaskCompletionSource WatchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> WatchedIds { get; } = [];
        public List<string> UnwatchedIds { get; } = [];

        public void Watch(string workspaceId, string workspacePath)
        {
            WatchedIds.Add(workspaceId);
            if (PauseWatch)
            {
                PauseWatch = false;
                WatchEntered.SetResult();
                ReleaseWatch.Task.GetAwaiter().GetResult();
            }
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
