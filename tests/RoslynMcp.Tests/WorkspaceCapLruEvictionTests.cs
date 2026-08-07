using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>parallel-mode-workspace-cap-lru-or-raise</c>:
/// <list type="bullet">
///   <item><description>The default <see cref="WorkspaceManagerOptions.MaxConcurrentWorkspaces"/> is raised to 16.</description></item>
///   <item><description><see cref="EvictPolicy.Lru"/> silently evicts the least-recently-used unlocked session when the cap is reached.</description></item>
///   <item><description><see cref="EvictPolicy.Strict"/> throws with <c>activeWorkspaces</c> and <c>lruCandidate</c> context.</description></item>
/// </list>
///
/// <para>
/// Also carries the <c>lru-eviction-gate-layer-execution</c> guard: when a gate reference is
/// wired, eviction runs under <see cref="WorkspaceExecutionGate"/>'s per-workspace writer lock, so
/// a workspace with a gated read in flight is NOT evicted underneath that reader — the eviction
/// blocks until the reader drains.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceCapLruEvictionTests
{
    private static string s_repositoryRootPath = null!;
    private static string s_sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        s_repositoryRootPath = TestFixtureFileSystem.FindRepositoryRoot();
        s_sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            s_repositoryRootPath,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    /// <summary>
    /// Validates that <see cref="WorkspaceManagerOptions.MaxConcurrentWorkspaces"/> defaults to 16
    /// and that the semaphore is initialised with that value so 16 independent loads can succeed
    /// without hitting the cap (verified via property, not by loading 16 real workspaces to keep
    /// the test fast).
    /// </summary>
    [TestMethod]
    public void DefaultMaxConcurrentWorkspaces_Is_16()
    {
        var opts = new WorkspaceManagerOptions();
        Assert.AreEqual(16, opts.MaxConcurrentWorkspaces,
            "Default cap must be 16 after parallel-mode-workspace-cap-lru-or-raise.");
    }

    /// <summary>
    /// When the cap is reached and <see cref="EvictPolicy.Lru"/> is requested, the workspace with
    /// the smallest <c>LastAccessedUtc</c> is evicted and the new load succeeds. The evicted
    /// workspace is no longer tracked.
    /// </summary>
    [TestMethod]
    public async Task LruEviction_WhenCapReached_EvictsLeastRecentlyUsed_AndSucceeds()
    {
        // Use a cap of 1 so a single loaded workspace saturates the semaphore.
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            // Load the first workspace — it now holds the single slot.
            var status1 = await manager.LoadAsync(path1, CancellationToken.None);
            Assert.IsTrue(manager.ContainsWorkspace(status1.WorkspaceId),
                "First workspace must be tracked after load.");

            // LRU load: should evict the first workspace and load the second.
            var status2 = await manager.LoadAsync(path2, EvictPolicy.Lru, CancellationToken.None);
            Assert.IsTrue(manager.ContainsWorkspace(status2.WorkspaceId),
                "Second workspace must be tracked after LRU load.");
            Assert.IsFalse(manager.ContainsWorkspace(status1.WorkspaceId),
                "First workspace must have been evicted by the LRU policy.");
            Assert.AreNotEqual(status1.WorkspaceId, status2.WorkspaceId,
                "Evicted and new workspaces must have distinct IDs.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// When the cap is reached and <see cref="EvictPolicy.Strict"/> is requested (the default),
    /// an <see cref="InvalidOperationException"/> is thrown whose message includes
    /// <c>activeWorkspaces</c> and <c>lruCandidate</c> fields for one-round-trip self-recovery.
    /// </summary>
    [TestMethod]
    public async Task StrictEviction_WhenCapReached_Throws_WithDiagnosticContext()
    {
        // Use a cap of 1 so a single loaded workspace saturates the semaphore.
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var status1 = await manager.LoadAsync(path1, CancellationToken.None);
            Assert.IsTrue(manager.ContainsWorkspace(status1.WorkspaceId),
                "First workspace must be tracked (sanity).");

            // Strict load: must throw because the cap is full.
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => manager.LoadAsync(path2, EvictPolicy.Strict, CancellationToken.None));

            // Message must carry activeWorkspaces JSON and lruCandidate for self-recovery.
            StringAssert.Contains(ex.Message, "activeWorkspaces",
                "Strict-mode error must include 'activeWorkspaces' field for agent self-recovery.");
            StringAssert.Contains(ex.Message, "lruCandidate",
                "Strict-mode error must include 'lruCandidate' field for agent self-recovery.");
            StringAssert.Contains(ex.Message, status1.WorkspaceId,
                "The active workspace ID must appear in the error to identify the eviction candidate.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// <c>lru-eviction-gate-layer-execution</c>: with a gate reference wired (as production DI
    /// does), cap-pressure LRU eviction runs under <see cref="WorkspaceExecutionGate"/>'s
    /// per-workspace WRITER lock, so it BLOCKS behind an in-flight gated reader instead of
    /// disposing the workspace underneath it.
    ///
    /// <para>
    /// Covers both failure modes from the row's acceptance criteria:
    /// <list type="number">
    ///   <item><description>
    ///   the evicted workspace is still tracked (and the triggering load still pending) for as
    ///   long as the reader holds the gate's reader lock; and
    ///   </description></item>
    ///   <item><description>
    ///   a <c>GetProject</c> call made from inside that gated read — the
    ///   <c>GetRequiredSession</c>-then-touch-<c>CurrentSolution</c> TOCTOU — no longer observes
    ///   <see cref="WorkspaceEvictedException"/>, because both paths now serialize on the same
    ///   per-workspace lock.
    ///   </description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Deterministic by construction, no <c>Task.Delay</c>: the reader signals that it holds the
    /// lock, the eviction-triggering load is STARTED (not awaited — awaiting it here would
    /// deadlock, since it now waits on the reader), the blocked state is asserted, and only then
    /// is the reader released.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task LruEviction_WhileGatedReadInFlight_BlocksUntilReaderDrains()
    {
        // Mutable indirection: the manager needs the gate and the gate needs the manager. The
        // Lazy defers the read of `gate` until first eviction, which is exactly the cycle-break
        // production DI performs with its Lazy<IWorkspaceExecutionGate> registration.
        WorkspaceExecutionGate? gate = null;

        // Cap of 1 so a single loaded workspace saturates the semaphore and the second load
        // must evict.
        using var manager = CreateManager(
            maxConcurrentWorkspaces: 1,
            evictionGate: new Lazy<IWorkspaceExecutionGate>(() => gate!));

        gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { RequestTimeout = TimeSpan.FromMinutes(5) },
            manager);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var status1 = await manager.LoadAsync(path1, CancellationToken.None);

            // Deterministic hand-off (no Task.Delay): readerHoldsLock signals that the gate's
            // reader lock for workspace1 is held; readerMayRelease lets the reader finish only
            // after the eviction-triggering load has been observed blocked.
            var readerHoldsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var readerMayRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var readerTask = gate.RunReadAsync<(Exception? Error, bool StillTracked)>(
                status1.WorkspaceId,
                async _ =>
                {
                    try
                    {
                        readerHoldsLock.SetResult();
                        await readerMayRelease.Task.WaitAsync(TestTimeout);

                        // Still inside the gated read — the reader lock has NOT been released, so
                        // a gate-respecting eviction cannot have run yet.
                        try
                        {
                            manager.GetProject(status1.WorkspaceId, "SampleLib");
                            return (null, manager.ContainsWorkspace(status1.WorkspaceId));
                        }
                        catch (Exception ex)
                        {
                            return (ex, manager.ContainsWorkspace(status1.WorkspaceId));
                        }
                    }
                    finally
                    {
                        // Never strand the eviction on a reader that failed before releasing.
                        readerMayRelease.TrySetResult();
                    }
                },
                CancellationToken.None);

            await readerHoldsLock.Task.WaitAsync(TestTimeout);

            // Start (do NOT await) the eviction-triggering load: it must now block on the
            // evicted candidate's writer lock until the reader above drains.
            var evictingLoad = manager.LoadAsync(path2, EvictPolicy.Lru, CancellationToken.None);

            Assert.IsTrue(manager.ContainsWorkspace(status1.WorkspaceId),
                "Eviction must not remove a workspace whose gate reader lock is still held.");
            Assert.IsFalse(evictingLoad.IsCompleted,
                "The triggering load cannot complete before the eviction it depends on, and the "
                + "eviction is blocked behind the in-flight gated reader.");

            readerMayRelease.SetResult();

            var (observed, stillTrackedDuringRead) = await readerTask.WaitAsync(TestTimeout);
            var status2 = await evictingLoad.WaitAsync(TestTimeout);

            Assert.IsNull(observed,
                "GetProject called from inside the gated read must not observe eviction now that "
                + "eviction serializes on the same per-workspace lock. Actual: "
                + (observed?.GetType().Name ?? "<none>"));
            Assert.IsTrue(stillTrackedDuringRead,
                "The workspace must still be tracked for the whole duration of the gated read.");

            Assert.IsFalse(manager.ContainsWorkspace(status1.WorkspaceId),
                "Once the reader drained, the LRU candidate must actually be evicted.");
            Assert.IsTrue(manager.ContainsWorkspace(status2.WorkspaceId),
                "The triggering load must have taken the slot freed by the eviction.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>Fail fast rather than hang the suite if a hand-off regresses into a deadlock.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);

    private static WorkspaceManager CreateManager(
        int maxConcurrentWorkspaces = 4,
        Lazy<IWorkspaceExecutionGate>? evictionGate = null)
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maxConcurrentWorkspaces },
            cacheStore: null,
            evictionGate: evictionGate);
    }
}
