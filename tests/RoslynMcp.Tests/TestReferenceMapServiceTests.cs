using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// dr-9-5-bug-pagination-001-has-no-pagination-filter-igno: regression coverage for
/// <see cref="TestReferenceMapService.BuildAsync"/> pagination + projectName scoping.
/// Service shipped without tests — these are the first, scoped to the I-23 defects.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class TestReferenceMapServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static TestReferenceMapService Service { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        Service = new TestReferenceMapService(WorkspaceManager);
    }

    [TestMethod]
    public async Task BuildAsync_DefaultPagination_ReturnsFullSetOnSmallSolution()
    {
        var result = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500, CancellationToken.None);

        Assert.AreEqual(0, result.Offset);
        Assert.AreEqual(500, result.Limit);
        Assert.IsFalse(result.HasMore,
            "Sample solution is small enough that a 500-entry page should absorb it.");
        Assert.AreEqual(
            result.TotalCoveredCount + result.TotalUncoveredCount,
            result.CoveredSymbols.Count + result.UncoveredSymbols.Count,
            "Returned entries should equal totals when HasMore is false.");
    }

    [TestMethod]
    public async Task BuildAsync_Limit1_TruncatesAndSurfacesHasMore()
    {
        var result = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 1, CancellationToken.None);

        Assert.AreEqual(1, result.Limit);
        Assert.AreEqual(1, result.CoveredSymbols.Count + result.UncoveredSymbols.Count,
            "Limit=1 must return exactly one entry across the combined covered+uncovered stream.");
        var totals = result.TotalCoveredCount + result.TotalUncoveredCount;
        if (totals > 1)
        {
            Assert.IsTrue(result.HasMore,
                $"HasMore must be true when totals ({totals}) exceed the returned page (1).");
        }
    }

    [TestMethod]
    public async Task BuildAsync_OffsetClampsToTotal_ReturnsEmptyPageNoHasMore()
    {
        var first = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500, CancellationToken.None);
        var totals = first.TotalCoveredCount + first.TotalUncoveredCount;

        var tail = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: totals + 50, limit: 10, CancellationToken.None);

        Assert.AreEqual(totals, tail.Offset, "Offset must clamp to the combined total.");
        Assert.AreEqual(0, tail.CoveredSymbols.Count + tail.UncoveredSymbols.Count);
        Assert.IsFalse(tail.HasMore);
    }

    [TestMethod]
    public async Task BuildAsync_CoveragePercentStable_AcrossPageSizes()
    {
        var full = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500, CancellationToken.None);
        var paged = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 1, CancellationToken.None);

        Assert.AreEqual(full.CoveragePercent, paged.CoveragePercent,
            "CoveragePercent is pegged to the full totals; page size must not change the verdict.");
    }

    [TestMethod]
    public async Task BuildAsync_ProjectName_ProductiveProject_FiltersCollectedSymbols()
    {
        // SampleLib is the productive project; filtering to it should shrink the productive
        // symbol set vs the unscoped sweep.
        var unscoped = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500, CancellationToken.None);
        var scoped = await Service.BuildAsync(
            WorkspaceId, projectName: "SampleLib", offset: 0, limit: 500, CancellationToken.None);

        var unscopedTotal = unscoped.TotalCoveredCount + unscoped.TotalUncoveredCount;
        var scopedTotal = scoped.TotalCoveredCount + scoped.TotalUncoveredCount;
        Assert.IsTrue(scopedTotal <= unscopedTotal,
            $"Scoped total ({scopedTotal}) must not exceed unscoped total ({unscopedTotal}) — filter should narrow, not expand.");
    }

    [TestMethod]
    public async Task BuildAsync_ProjectName_UnknownName_Throws()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service.BuildAsync(
                WorkspaceId, projectName: "DefinitelyNotARealProject", offset: 0, limit: 10, CancellationToken.None));
    }
}
