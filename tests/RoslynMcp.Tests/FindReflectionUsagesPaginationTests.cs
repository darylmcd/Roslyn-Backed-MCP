using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the <c>find_reflection_usages</c> tool's paging + summary envelope.
/// Repro for gh #760 (compact-paged-high-volume-analysis-results): a 256-site
/// reflection-heavy solution previously emitted a 153 KB unbounded payload
/// because the wrapper accepted no offset/limit/summary parameters. The fix
/// adds <c>offset</c>, <c>limit</c> (default 200), <c>summary</c> at the wrapper
/// layer — collect-all + Skip/Take, mirroring <c>find_type_usages</c>.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindReflectionUsagesPaginationTests : SharedWorkspaceTestBase
{
    private const string ReflectionFixtureFileName = "ReflectionUsageFixture.cs";

    // Synthesises a deterministic set of reflection sites that span every UsageKind
    // returned by ClassifyReflectionUsage + the typeof() branch. The mix is chosen
    // so the test can assert distinct slices for different offsets without depending
    // on hash-ordered grouping. Total sites:
    //   - 6 × typeof(string/int/bool/double/object/byte[])
    //   - 4 × Activator.CreateInstance(...)
    //   - 4 × Type.GetMethod / GetProperty / GetField / GetMember
    //   - 3 × Reflection.Invoke / PropertyInfo.GetValue / FieldInfo.SetValue
    //   - 3 × Assembly.Load / Assembly.GetType / Type.GetType
    //   Total = 20, comfortably above the small limit values the test uses.
    private const string ReflectionFixtureSource = """
namespace SampleLib;

using System;
using System.Reflection;

public static class ReflectionUsageFixture
{
    public static object?[] AllReflectionSites()
    {
        var typeofs = new Type[]
        {
            typeof(string),
            typeof(int),
            typeof(bool),
            typeof(double),
            typeof(object),
            typeof(byte[]),
        };

        // Activator.CreateInstance — 4 sites
        var a1 = Activator.CreateInstance(typeof(object));
        var a2 = Activator.CreateInstance(typeof(string), new object[] { "abc" });
        var a3 = Activator.CreateInstance<int>();
        var a4 = Activator.CreateInstance(typeof(int).Assembly.GetTypes()[0]);

        // Type.GetMethod / GetProperty / GetField / GetMember — 4 sites
        var m = typeof(string).GetMethod("ToUpper", Type.EmptyTypes);
        var p = typeof(string).GetProperty("Length");
        var f = typeof(int).GetField("MaxValue");
        var mem = typeof(string).GetMember("Empty");

        // Reflection.Invoke / PropertyInfo.GetValue / FieldInfo.SetValue — 3 sites
        var invoked = m?.Invoke("hello", null);
        var propVal = p?.GetValue("hi");
        f?.SetValue(null, 0);

        // Assembly.Load / Assembly.GetType / Type.GetType — 3 sites
        var asm = Assembly.Load("System.Runtime");
        var t1 = asm.GetType("System.Object");
        var t2 = Type.GetType("System.String");

        return new object?[] { typeofs, a1, a2, a3, a4, m, p, f, mem, invoked, propVal, asm, t1, t2 };
    }
}
""";

    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Materializes a fresh sample-solution copy with the synthetic reflection fixture
    /// in <c>SampleLib/ReflectionUsageFixture.cs</c>, loads the isolated workspace, and
    /// hands the caller the workspaceId + cleanup hook. Pattern matches the per-test
    /// fixture-copy approach used by <see cref="CohesionAnalysisTests"/> — avoids the
    /// MSTest parallel-class race that wiped the temp tree when fixture setup lived
    /// in <c>[ClassInitialize]</c>.
    /// </summary>
    private static async Task<(string WorkspaceId, string RootPath)> CreateReflectionFixtureAsync()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var fixturePath = Path.Combine(copiedRoot, "SampleLib", ReflectionFixtureFileName);
        await File.WriteAllTextAsync(fixturePath, ReflectionFixtureSource, CancellationToken.None);

        var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (status.WorkspaceId, copiedRoot);
    }

    /// <summary>
    /// Sanity-anchor: with limit large enough to fit everything the fixture must report
    /// a positive count and totalCount across multiple UsageKind buckets. If this falls
    /// to zero the synthetic fixture is no longer compiling — every other assertion in
    /// this class would silently pass on an empty list, so we anchor totals first.
    /// </summary>
    [TestMethod]
    public async Task FindReflectionUsages_FixtureProducesPositiveTotalAcrossUsageKinds()
    {
        var (workspaceId, rootPath) = await CreateReflectionFixtureAsync();
        try
        {
            var json = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: 500,
                summary: false,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var totalCount = root.GetProperty("totalCount").GetInt32();
            Assert.IsTrue(totalCount > 10,
                $"Expected the synthetic ReflectionUsageFixture to produce > 10 reflection sites, got {totalCount}. " +
                $"If this fails the fixture source likely no longer compiles.");

            var usagesByKind = root.GetProperty("usagesByKind");
            var distinctKinds = 0;
            foreach (var _kv in usagesByKind.EnumerateObject())
            {
                distinctKinds++;
            }
            Assert.IsTrue(distinctKinds >= 4,
                $"Expected reflection fixture to span >= 4 distinct UsageKinds, got {distinctKinds}.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    /// <summary>
    /// Bounded-collection contract: when <c>limit</c> &lt; <c>totalCount</c>, the response
    /// must report <c>hasMore=true</c>, <c>count==limit</c>, and the same <c>totalCount</c>
    /// as the unpaged call. This is the core fix for gh #760: previously the wrapper returned
    /// every hit regardless of payload size.
    /// </summary>
    [TestMethod]
    public async Task FindReflectionUsages_LimitSmallerThanTotal_ReportsHasMoreAndBoundedCount()
    {
        const int Limit = 3;

        var (workspaceId, rootPath) = await CreateReflectionFixtureAsync();
        try
        {
            var json = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: Limit,
                summary: false,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var count = root.GetProperty("count").GetInt32();
            var totalCount = root.GetProperty("totalCount").GetInt32();
            var hasMore = root.GetProperty("hasMore").GetBoolean();

            Assert.AreEqual(Limit, count, "count must equal the requested limit when totalCount > limit.");
            Assert.IsTrue(totalCount > Limit,
                $"Fixture totalCount ({totalCount}) must exceed Limit ({Limit}) for this test to be meaningful.");
            Assert.IsTrue(hasMore, "hasMore must be true when totalCount > offset + count.");

            // Echoed paging knobs let callers route follow-up requests off the same envelope shape.
            Assert.AreEqual(0, root.GetProperty("offset").GetInt32());
            Assert.AreEqual(Limit, root.GetProperty("limit").GetInt32());
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    /// <summary>
    /// Pagination is deterministic: the first window (offset=0, limit=N) and the second
    /// (offset=N, limit=N) must not return the same item set. We compare the JSON of the
    /// raw usagesByKind dictionaries via stringification — different slices produce
    /// different JSON. Both windows must report the same totalCount.
    /// </summary>
    [TestMethod]
    public async Task FindReflectionUsages_DifferentOffsets_ReturnDifferentSlicesSameTotal()
    {
        const int Limit = 5;

        var (workspaceId, rootPath) = await CreateReflectionFixtureAsync();
        try
        {
            var firstJson = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: Limit,
                summary: false,
                ct: CancellationToken.None);

            var secondJson = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: Limit,
                limit: Limit,
                summary: false,
                ct: CancellationToken.None);

            using var first = JsonDocument.Parse(firstJson);
            using var second = JsonDocument.Parse(secondJson);

            var firstTotal = first.RootElement.GetProperty("totalCount").GetInt32();
            var secondTotal = second.RootElement.GetProperty("totalCount").GetInt32();
            Assert.AreEqual(firstTotal, secondTotal,
                "totalCount must be stable across paging requests against the same fixture.");

            // Compare the materialized usagesByKind sections. The fixture has > 2*Limit items
            // so the two windows MUST diverge.
            var firstUsages = first.RootElement.GetProperty("usagesByKind").GetRawText();
            var secondUsages = second.RootElement.GetProperty("usagesByKind").GetRawText();
            Assert.AreNotEqual(firstUsages, secondUsages,
                "Different offset windows must materialize different reflection-site slices.");

            Assert.AreEqual(0, first.RootElement.GetProperty("offset").GetInt32());
            Assert.AreEqual(Limit, second.RootElement.GetProperty("offset").GetInt32());
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    /// <summary>
    /// Summary mode drops the item arrays entirely: the response carries
    /// <c>summary=true</c>, <c>count=0</c>, the same <c>totalCount</c> as the unsummarized
    /// call, no <c>usagesByKind</c> array, and a <c>usageKindCounts</c> dictionary whose
    /// values are computed off the FULL result set (not the paged slice). The whole point
    /// is a payload-size escape valve for solutions where even a paginated default still
    /// exceeds the MCP cap.
    /// </summary>
    [TestMethod]
    public async Task FindReflectionUsages_SummaryMode_DropsItemArraysReturnsAggregateCounts()
    {
        var (workspaceId, rootPath) = await CreateReflectionFixtureAsync();
        try
        {
            var summaryJson = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: 1,
                summary: true,
                ct: CancellationToken.None);

            var unpagedJson = await AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: 500,
                summary: false,
                ct: CancellationToken.None);

            using var summaryDoc = JsonDocument.Parse(summaryJson);
            using var unpagedDoc = JsonDocument.Parse(unpagedJson);

            Assert.IsTrue(summaryDoc.RootElement.GetProperty("summary").GetBoolean());
            Assert.AreEqual(0, summaryDoc.RootElement.GetProperty("count").GetInt32(),
                "Summary mode must report count=0 because no item rows are emitted.");

            var unpagedTotal = unpagedDoc.RootElement.GetProperty("totalCount").GetInt32();
            var summaryTotal = summaryDoc.RootElement.GetProperty("totalCount").GetInt32();
            Assert.AreEqual(unpagedTotal, summaryTotal,
                "Summary mode totalCount must match the full-collection total (computed off the full result set).");

            // usageKindCounts dictionary mirrors usagesByKind keys but contains integer counts only.
            var counts = summaryDoc.RootElement.GetProperty("usageKindCounts");
            var summed = 0;
            foreach (var entry in counts.EnumerateObject())
            {
                summed += entry.Value.GetInt32();
            }
            Assert.AreEqual(summaryTotal, summed,
                "Sum of usageKindCounts must equal totalCount — counts are aggregated off the full set.");

            // No item-bearing field in summary mode.
            Assert.IsFalse(summaryDoc.RootElement.TryGetProperty("usagesByKind", out _),
                "Summary mode must omit the usagesByKind item arrays.");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    /// <summary>
    /// Invalid pagination must surface a parameter-validation error, not silently produce
    /// an envelope with negative offset or zero limit. Matches the same contract enforced
    /// by <c>project_diagnostics</c> via <c>ParameterValidation.ValidatePagination</c>.
    /// Uses the shared sample workspace directly — no fixture copy needed because the
    /// validation throws before any workspace work happens.
    /// </summary>
    [TestMethod]
    public async Task FindReflectionUsages_InvalidPagination_ThrowsArgumentException()
    {
        var workspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);

        var negativeOffsetEx = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: -1,
                limit: 10,
                summary: false,
                ct: CancellationToken.None));
        StringAssert.Contains(negativeOffsetEx.Message, "offset");

        var zeroLimitEx = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            AdvancedAnalysisTools.FindReflectionUsages(
                gate: WorkspaceExecutionGate,
                codePatternAnalyzer: CodePatternAnalyzer,
                workspaceId: workspaceId,
                projectName: "SampleLib",
                offset: 0,
                limit: 0,
                summary: false,
                ct: CancellationToken.None));
        StringAssert.Contains(zeroLimitEx.Message, "limit");
    }
}
