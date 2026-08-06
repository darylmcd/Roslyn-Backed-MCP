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
/// Also carries the <c>lru-eviction-concurrent-reader-safety-overstated</c> characterization
/// guard: the LRU scan does not consult <see cref="WorkspaceExecutionGate"/>, so a workspace with
/// a gated read in flight is still evicted underneath that reader.
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
    /// <c>lru-eviction-concurrent-reader-safety-overstated</c>: characterization guard pinning the
    /// documented gap between the LRU eviction scan and <see cref="WorkspaceExecutionGate"/>.
    ///
    /// <para>
    /// The scan filters candidates on <c>LoadLock.CurrentCount &gt; 0</c>. <c>LoadLock</c> is
    /// acquired only by <c>LoadIntoSessionAsync</c>, so it excludes sessions that are mid-load —
    /// and nothing else. It never consults the gate's per-workspace reader/writer lock, so a
    /// workspace with a gated read in flight is evicted and disposed underneath that reader,
    /// which then observes <see cref="WorkspaceEvictedException"/> on its next lookup
    /// (<c>Close()</c> removes the session and records the eviction before disposing it, so
    /// <c>GetRequiredSession</c> throws before any disposed <c>MSBuildWorkspace</c> is touched).
    /// </para>
    ///
    /// <para>
    /// This asserts the CURRENT (unsafe) behaviour deliberately. If the gap is ever closed — by
    /// moving eviction execution up into the gate layer and taking
    /// <c>IWorkspaceExecutionGate.RunWriteAsync</c> on the evicted candidate — this test must fail
    /// and be rewritten to assert the eviction blocks until the reader drains.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task LruEviction_WhileGatedReadInFlight_EvictsAnyway_AndReaderObservesEviction()
    {
        // Cap of 1 so a single loaded workspace saturates the semaphore and the second load
        // must evict.
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);

        // Real gate + real manager, wired exactly as production DI does.
        var gate = new WorkspaceExecutionGate(
            new ExecutionGateOptions { RequestTimeout = TimeSpan.FromMinutes(5) },
            manager);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var status1 = await manager.LoadAsync(path1, CancellationToken.None);

            // Deterministic hand-off (no Task.Delay): readerHoldsLock signals that the gate's
            // reader lock for workspace1 is held; evictionCompleted releases the reader only
            // after the LRU eviction has already run.
            var readerHoldsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var evictionCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var readerTask = gate.RunReadAsync<Exception?>(
                status1.WorkspaceId,
                async _ =>
                {
                    readerHoldsLock.SetResult();
                    await evictionCompleted.Task;

                    // Still inside the gated read — the reader lock has NOT been released. A
                    // lifecycle transition that respected the gate could not have run yet.
                    try
                    {
                        manager.GetProject(status1.WorkspaceId, "SampleLibrary");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                },
                CancellationToken.None);

            await readerHoldsLock.Task;

            // THE GAP: the LRU scan ignores the gate, so this evicts workspace1 out from under
            // the in-flight reader instead of waiting for it to drain.
            var status2 = await manager.LoadAsync(path2, EvictPolicy.Lru, CancellationToken.None);

            Assert.IsFalse(manager.ContainsWorkspace(status1.WorkspaceId),
                "Characterization: LRU eviction currently ignores WorkspaceExecutionGate's reader "
                + "lock and evicts a workspace that has a read in flight.");
            Assert.IsTrue(manager.ContainsWorkspace(status2.WorkspaceId),
                "The triggering load must have taken the slot freed by the eviction.");

            evictionCompleted.SetResult();
            var observed = await readerTask;

            Assert.IsInstanceOfType<WorkspaceEvictedException>(observed,
                "The in-flight reader must observe WorkspaceEvictedException on its next lookup — "
                + "Close() removes the session from the live set and records the eviction before "
                + "disposing it, so GetRequiredSession throws before any disposed MSBuildWorkspace "
                + "is reached. Actual: " + (observed?.GetType().Name ?? "<no exception thrown>"));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    private static WorkspaceManager CreateManager(int maxConcurrentWorkspaces = 4)
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maxConcurrentWorkspaces });
    }
}
