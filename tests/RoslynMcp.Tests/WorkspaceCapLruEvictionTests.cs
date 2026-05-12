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
            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
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

    private static WorkspaceManager CreateManager(int maxConcurrentWorkspaces = 4)
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maxConcurrentWorkspaces });
    }
}
