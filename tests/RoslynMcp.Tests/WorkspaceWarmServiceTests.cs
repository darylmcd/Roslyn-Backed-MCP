namespace RoslynMcp.Tests;

/// <summary>
/// Integration tests for <see cref="Roslyn.Services.WorkspaceWarmService"/>. Uses
/// <see cref="IsolatedWorkspaceTestBase"/> so each test starts from a cold workspace
/// (fresh <c>MSBuildWorkspace</c> → no projects in Roslyn's compilation cache) and can
/// assert the cold-to-warm transition without cross-test contamination from other
/// fixtures that may have already forced compilations.
/// </summary>
[TestClass]
public sealed class WorkspaceWarmServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task WarmAsync_FirstCall_ForcesColdCompilations()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var result = await WorkspaceWarmService.WarmAsync(
            workspace.WorkspaceId,
            projects: null,
            CancellationToken.None);

        Assert.AreEqual(workspace.WorkspaceId, result.WorkspaceId, "Workspace id should round-trip.");
        Assert.IsTrue(
            result.ProjectsWarmed.Count > 0,
            $"Expected projectsWarmed.Count > 0; got {result.ProjectsWarmed.Count}.");
        // Wall-clock elapsedMs is too brittle for a correctness assertion — a fast CI runner
        // can complete cold compilations well under 100ms (observed: 23ms). The semantic
        // signal that real work happened is ColdCompilationCount > 0.
        Assert.IsTrue(
            result.ColdCompilationCount > 0,
            $"Expected coldCompilationCount > 0 on a fresh workspace; got {result.ColdCompilationCount}.");
    }

    [TestMethod]
    public async Task WarmAsync_SecondCall_NoCold_FasterThanFirst()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);

        var first = await WorkspaceWarmService.WarmAsync(
            workspace.WorkspaceId,
            projects: null,
            CancellationToken.None);

        // Sanity: the first call must have done real work, otherwise the warm/warm
        // comparison below is meaningless.
        Assert.IsTrue(
            first.ColdCompilationCount > 0,
            "Precondition failed: first warm should report at least one cold project.");

        var second = await WorkspaceWarmService.WarmAsync(
            workspace.WorkspaceId,
            projects: null,
            CancellationToken.None);

        Assert.AreEqual(
            0,
            second.ColdCompilationCount,
            "Repeat warm on an unchanged workspace should see every project already cached.");
        Assert.IsTrue(
            second.ElapsedMs < first.ElapsedMs / 10,
            $"Expected second warm to be at least 10× faster than first: first={first.ElapsedMs}ms second={second.ElapsedMs}ms.");
    }
}
