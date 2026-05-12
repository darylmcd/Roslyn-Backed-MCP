using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>symbol-search-pagination</c>: <c>symbol_search</c> now supports
/// <c>offset</c> / <c>limit</c> pagination (max 200) with <c>totalCount</c> and <c>hasMore</c>
/// in the response envelope.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolSearchPaginationTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ── pagination envelope fields ──────────────────────────────────────────

    [TestMethod]
    public async Task SymbolSearch_ResponseContainsPaginationFields()
    {
        // Any non-empty query into SampleSolution should return the pagination envelope.
        var json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 5, offset: 0,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("count", out var count), "count field must be present");
        Assert.IsTrue(root.TryGetProperty("totalCount", out var totalCount), "totalCount field must be present");
        Assert.IsTrue(root.TryGetProperty("hasMore", out _), "hasMore field must be present");
        Assert.IsTrue(root.TryGetProperty("offset", out var offsetProp), "offset field must be present");
        Assert.IsTrue(root.TryGetProperty("limit", out var limitProp), "limit field must be present");
        Assert.IsTrue(root.TryGetProperty("symbols", out var symbols), "symbols field must be present");
        Assert.AreEqual(JsonValueKind.Array, symbols.ValueKind);

        Assert.AreEqual(0, offsetProp.GetInt32(), "offset must echo the requested value");
        Assert.AreEqual(5, limitProp.GetInt32(), "limit must echo the requested value");
        Assert.IsTrue(totalCount.GetInt32() >= count.GetInt32(),
            "totalCount must be >= count (paged slice size)");
        Assert.IsTrue(count.GetInt32() <= 5, "count must not exceed the requested limit");
    }

    // ── offset slicing ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task SymbolSearch_OffsetSlicesResults()
    {
        // Fetch first two pages and verify they are disjoint and together cover page 0.
        var page0Json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 3, offset: 0,
                ct: CancellationToken.None));

        var page1Json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 3, offset: 3,
                ct: CancellationToken.None));

        using var doc0 = JsonDocument.Parse(page0Json);
        using var doc1 = JsonDocument.Parse(page1Json);

        var symbols0 = doc0.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(s => s.GetProperty("fullyQualifiedName").GetString())
            .ToHashSet(StringComparer.Ordinal);

        var symbols1 = doc1.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(s => s.GetProperty("fullyQualifiedName").GetString())
            .ToHashSet(StringComparer.Ordinal);

        // Consecutive pages must be disjoint (no repeated FQNs across offset=0 and offset=3).
        var overlap = symbols0.Intersect(symbols1).ToList();
        Assert.AreEqual(0, overlap.Count,
            $"Pages must be disjoint. Overlapping FQNs: {string.Join(", ", overlap)}");

        // totalCount must be the same on both pages (same query, same underlying result set).
        var total0 = doc0.RootElement.GetProperty("totalCount").GetInt32();
        var total1 = doc1.RootElement.GetProperty("totalCount").GetInt32();
        Assert.AreEqual(total0, total1,
            "totalCount must be identical across pages of the same query");
    }

    // ── hasMore flag ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SymbolSearch_HasMore_FalseOnLastPage()
    {
        // First page: find out how many Animal symbols are present.
        var probeJson = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 200, offset: 0,
                ct: CancellationToken.None));

        using var probeDoc = JsonDocument.Parse(probeJson);
        var total = probeDoc.RootElement.GetProperty("totalCount").GetInt32();
        var hasMoreProbe = probeDoc.RootElement.GetProperty("hasMore").GetBoolean();
        Assert.IsFalse(hasMoreProbe,
            "When limit=200 covers all results, hasMore must be false");
        Assert.IsTrue(total >= 1, "SampleSolution must contain at least one 'Animal' symbol");
    }

    [TestMethod]
    public async Task SymbolSearch_HasMore_TrueWhenResultsExceedLimit()
    {
        // Use a very small limit to force hasMore=true when the solution has multiple Animal symbols.
        var json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 1, offset: 0,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        var total = doc.RootElement.GetProperty("totalCount").GetInt32();
        var hasMore = doc.RootElement.GetProperty("hasMore").GetBoolean();

        if (total > 1)
        {
            // SampleSolution has multiple Animal symbols — hasMore must be true.
            Assert.IsTrue(hasMore,
                $"With limit=1 and totalCount={total}, hasMore must be true");
        }
        else
        {
            // Edge case: only 1 match — hasMore must be false.
            Assert.IsFalse(hasMore,
                "When exactly 1 result and limit=1 there is nothing more, hasMore must be false");
        }
    }

    // ── limit = 200 max ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task SymbolSearch_LimitExceeds200_ThrowsArgumentException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 201, offset: 0,
                ct: CancellationToken.None);
        });
    }

    // ── negative offset ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task SymbolSearch_NegativeOffset_ThrowsArgumentException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
        {
            await SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "Animal",
                projectName: null, kind: null, @namespace: null,
                limit: 10, offset: -1,
                ct: CancellationToken.None);
        });
    }

    // ── empty-query envelope carries pagination fields ───────────────────────

    [TestMethod]
    public async Task SymbolSearch_EmptyQuery_EnvelopeHasPaginationFields()
    {
        var json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                server: null!, WorkspaceExecutionGate, SymbolSearchService, WorkspaceId,
                query: "",
                projectName: null, kind: null, @namespace: null,
                limit: 50, offset: 0,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(0, root.GetProperty("count").GetInt32());
        Assert.AreEqual(0, root.GetProperty("totalCount").GetInt32());
        Assert.IsFalse(root.GetProperty("hasMore").GetBoolean());
        Assert.AreEqual(0, root.GetProperty("offset").GetInt32());
        Assert.AreEqual(50, root.GetProperty("limit").GetInt32());
    }
}
