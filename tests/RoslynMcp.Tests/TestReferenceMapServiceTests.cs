using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// dr-9-5-bug-pagination-001-has-no-pagination-filter-igno: regression coverage for
/// <see cref="TestReferenceMapService.BuildAsync"/> pagination + projectName scoping.
/// Service shipped without tests — these are the first, scoped to the I-23 defects.
///
/// <para>
/// gh #774 / test-reference-map-pagination-only-covers-covered-symbols: extended with mock-drift
/// cap coverage. Because <see cref="TestReferenceMapService.BuildPaginatedResult"/> is the only
/// seam that exercises the per-collection cap and <c>DetectMockDriftAsync</c> builds drift
/// findings from live Roslyn data (the sample solution does not produce pathological cases
/// organically), the cap tests exercise the <c>internal static</c> seam directly with synthetic
/// inputs. <c>InternalsVisibleTo("RoslynMcp.Tests")</c> is already declared in
/// <c>RoslynMcp.Roslyn.csproj</c>.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class TestReferenceMapServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static TestReferenceMapService Service { get; set; } = null!;

    private const int DefaultMaxMockDriftWarnings = 50;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        Service = new TestReferenceMapService(WorkspaceManager, new CompilationCache(WorkspaceManager));
    }

    [TestMethod]
    public async Task BuildAsync_DefaultPagination_ReturnsFullSetOnSmallSolution()
    {
        var result = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);

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
            WorkspaceId, projectName: null, offset: 0, limit: 1,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);

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
            WorkspaceId, projectName: null, offset: 0, limit: 500,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);
        var totals = first.TotalCoveredCount + first.TotalUncoveredCount;

        var tail = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: totals + 50, limit: 10,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);

        Assert.AreEqual(totals, tail.Offset, "Offset must clamp to the combined total.");
        Assert.AreEqual(0, tail.CoveredSymbols.Count + tail.UncoveredSymbols.Count);
        Assert.IsFalse(tail.HasMore);
    }

    [TestMethod]
    public async Task BuildAsync_CoveragePercentStable_AcrossPageSizes()
    {
        var full = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);
        var paged = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 1,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);

        Assert.AreEqual(full.CoveragePercent, paged.CoveragePercent,
            "CoveragePercent is pegged to the full totals; page size must not change the verdict.");
    }

    [TestMethod]
    public async Task BuildAsync_ProjectName_ProductiveProject_FiltersCollectedSymbols()
    {
        // SampleLib is the productive project; filtering to it should shrink the productive
        // symbol set vs the unscoped sweep while still scanning the solution's test projects.
        var unscoped = await Service.BuildAsync(
            WorkspaceId, projectName: null, offset: 0, limit: 500,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);
        var scoped = await Service.BuildAsync(
            WorkspaceId, projectName: "SampleLib", offset: 0, limit: 500,
            maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None);

        var unscopedTotal = unscoped.TotalCoveredCount + unscoped.TotalUncoveredCount;
        var scopedTotal = scoped.TotalCoveredCount + scoped.TotalUncoveredCount;
        Assert.IsTrue(scopedTotal <= unscopedTotal,
            $"Scoped total ({scopedTotal}) must not exceed unscoped total ({unscopedTotal}) — filter should narrow, not expand.");
        CollectionAssert.Contains(
            scoped.InspectedTestProjects.ToList(),
            "SampleLib.Tests",
            "Productive-project scope must still scan eligible test projects.");
        Assert.IsFalse(
            scoped.Notes.Any(note => note.Contains("No test projects detected", StringComparison.Ordinal)),
            "A productive-project filter must not report that no tests exist when the workspace has test projects.");
    }

    [TestMethod]
    public async Task BuildAsync_ProjectName_UnknownName_Throws()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service.BuildAsync(
                WorkspaceId, projectName: "DefinitelyNotARealProject", offset: 0, limit: 10,
                maxMockDriftWarnings: DefaultMaxMockDriftWarnings, CancellationToken.None));
    }

    // gh #774 / test-reference-map-pagination-only-covers-covered-symbols: cap MockDriftWarnings.
    // Tests exercise the internal static seam directly because DetectMockDriftAsync builds drift
    // from live Roslyn data — the sample solution does not produce a 50+ warning case organically.
    // InternalsVisibleTo("RoslynMcp.Tests") is already wired in RoslynMcp.Roslyn.csproj.

    [TestMethod]
    public void BuildPaginatedResult_MockDriftCap_DefaultCapOf50_WhenMoreExist()
    {
        var mockDrift = SyntheticMockDrift(count: 109);

        var result = TestReferenceMapService.BuildPaginatedResult(
            coveredAll: Array.Empty<CoveredSymbolDto>(),
            uncoveredAll: Array.Empty<string>(),
            offset: 0,
            limit: 200,
            maxMockDriftWarnings: 50,
            scannedTestProjects: Array.Empty<string>(),
            notes: Array.Empty<string>(),
            mockDrift: mockDrift);

        Assert.AreEqual(50, result.MockDriftWarnings.Count,
            "Cap of 50 must truncate the 109-entry collection to 50 entries.");
        Assert.AreEqual(109, result.TotalMockDriftCount,
            "TotalMockDriftCount must always reflect the full pre-cap count.");
        Assert.IsTrue(result.HasMoreMockDrift,
            "HasMoreMockDrift must be true when the cap truncated the collection.");
    }

    [TestMethod]
    public void BuildPaginatedResult_MockDriftCap_Zero_ReturnsNoneWithMetadata()
    {
        var mockDrift = SyntheticMockDrift(count: 7);

        var result = TestReferenceMapService.BuildPaginatedResult(
            coveredAll: Array.Empty<CoveredSymbolDto>(),
            uncoveredAll: Array.Empty<string>(),
            offset: 0,
            limit: 200,
            maxMockDriftWarnings: 0,
            scannedTestProjects: Array.Empty<string>(),
            notes: Array.Empty<string>(),
            mockDrift: mockDrift);

        Assert.AreEqual(0, result.MockDriftWarnings.Count,
            "maxMockDriftWarnings=0 must return an empty drift list.");
        Assert.AreEqual(7, result.TotalMockDriftCount,
            "TotalMockDriftCount must always reflect the full count even when the cap is 0.");
        Assert.IsTrue(result.HasMoreMockDrift,
            "HasMoreMockDrift must be true when warnings exist but the cap returned none — callers still need to know drift was detected.");
    }

    [TestMethod]
    public void BuildPaginatedResult_MockDriftCap_NoTruncation_HasMoreMockDriftFalse()
    {
        var mockDrift = SyntheticMockDrift(count: 3);

        var result = TestReferenceMapService.BuildPaginatedResult(
            coveredAll: Array.Empty<CoveredSymbolDto>(),
            uncoveredAll: Array.Empty<string>(),
            offset: 0,
            limit: 200,
            maxMockDriftWarnings: 50,
            scannedTestProjects: Array.Empty<string>(),
            notes: Array.Empty<string>(),
            mockDrift: mockDrift);

        Assert.AreEqual(3, result.MockDriftWarnings.Count,
            "Cap above the actual count must pass all entries through.");
        Assert.AreEqual(3, result.TotalMockDriftCount);
        Assert.IsFalse(result.HasMoreMockDrift,
            "HasMoreMockDrift must be false when no truncation occurred.");
    }

    [TestMethod]
    public void BuildPaginatedResult_MockDriftCap_AboveUpperBound_Clamps()
    {
        // The cap is clamped to [0, 500]; an out-of-range request should not over-allocate.
        var mockDrift = SyntheticMockDrift(count: 600);

        var result = TestReferenceMapService.BuildPaginatedResult(
            coveredAll: Array.Empty<CoveredSymbolDto>(),
            uncoveredAll: Array.Empty<string>(),
            offset: 0,
            limit: 200,
            maxMockDriftWarnings: 5000,
            scannedTestProjects: Array.Empty<string>(),
            notes: Array.Empty<string>(),
            mockDrift: mockDrift);

        Assert.AreEqual(500, result.MockDriftWarnings.Count,
            "maxMockDriftWarnings must clamp to 500 even when callers request more.");
        Assert.AreEqual(600, result.TotalMockDriftCount);
        Assert.IsTrue(result.HasMoreMockDrift,
            "HasMoreMockDrift must be true after a clamp-induced truncation.");
    }

    /// <summary>
    /// Produce a deterministic synthetic <see cref="MockDriftWarningDto"/> list. The exact
    /// contents are irrelevant to the cap-logic tests — only <see cref="IReadOnlyList{T}.Count"/>
    /// matters — but distinct strings per row keep failure messages readable when an assertion
    /// trips and dumps the collection.
    /// </summary>
    private static IReadOnlyList<MockDriftWarningDto> SyntheticMockDrift(int count)
    {
        var warnings = new MockDriftWarningDto[count];
        for (var i = 0; i < count; i++)
        {
            warnings[i] = new MockDriftWarningDto(
                TestClassDisplay: $"Test.Class{i}",
                MockedInterfaceDisplay: $"IService{i}",
                MissingStubMethod: $"IService{i}.Method{i}",
                ProductionCallerDisplay: $"file{i}.cs:{i + 1}");
        }
        return warnings;
    }
}
