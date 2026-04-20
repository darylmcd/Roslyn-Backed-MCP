using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for the v3 top-10 remediation pass (post-v1.15.0): smoke tests for the
/// symbol-search-payload-meta response shape wrap, the filePaths parameter on
/// get_complexity_metrics, and the new format_check tool.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class Top10V3RegressionTests : IsolatedWorkspaceTestBase
{
    private static string _workspaceId = string.Empty;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        _workspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath);
    }

    // ── symbol-search-payload-meta: _meta now present on symbol_search ──

    [TestMethod]
    public async Task SymbolSearch_ResponseHasMetaAndCountEnvelope()
    {
        var json = await ToolExecutionTestHarness.RunAsync(
            "symbol_search",
            () => SymbolTools.SearchSymbols(
                WorkspaceExecutionGate, SymbolSearchService, _workspaceId,
                query: "AnimalService",
                projectName: null, kind: null, @namespace: null, limit: 10,
                CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("count", out var count),
            "symbol_search response must be an object with a 'count' field (was a bare array pre-fix — which silently dropped _meta).");
        Assert.IsTrue(count.GetInt32() >= 1);
        Assert.IsTrue(doc.RootElement.TryGetProperty("symbols", out _),
            "symbol_search response must expose the list under 'symbols'.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("_meta", out _),
            "symbol_search response must now carry _meta — the whole point of the shape wrap.");
    }

    // ── roslyn-mcp-complexity-subset-rerun: filePaths list parameter ──

    [TestMethod]
    public async Task ComplexityMetrics_FilePathsList_FiltersToThoseFiles()
    {
        var animalServicePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "AnimalService.cs");
        var refactoringProbePath = Path.Combine(Path.GetDirectoryName(SampleSolutionPath)!, "SampleLib", "RefactoringProbe.cs");

        var json = await AdvancedAnalysisTools.GetComplexityMetrics(
            WorkspaceExecutionGate, CodeMetricsService, _workspaceId,
            filePath: null,
            filePaths: new[] { animalServicePath, refactoringProbePath },
            projectName: null,
            minComplexity: null,
            limit: 100,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var metrics = doc.RootElement.GetProperty("metrics").EnumerateArray().ToList();
        Assert.IsTrue(metrics.Count > 0, "Expected results from the 2-file filter.");

        // Every returned method must belong to one of the two filtered files.
        foreach (var m in metrics)
        {
            var fp = m.GetProperty("filePath").GetString();
            Assert.IsTrue(
                fp?.EndsWith("AnimalService.cs", StringComparison.OrdinalIgnoreCase) == true ||
                fp?.EndsWith("RefactoringProbe.cs", StringComparison.OrdinalIgnoreCase) == true,
                $"Got a result outside the filtered filePaths set: {fp}");
        }
    }

    [TestMethod]
    public async Task ComplexityMetrics_EmptyFilePathsTreatedAsNoFilter()
    {
        var json = await AdvancedAnalysisTools.GetComplexityMetrics(
            WorkspaceExecutionGate, CodeMetricsService, _workspaceId,
            filePath: null,
            filePaths: Array.Empty<string>(),
            projectName: "SampleLib",
            minComplexity: null,
            limit: 200,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        Assert.IsTrue(count > 0, "Empty filePaths list should behave as 'no filter' — expected >0 results from the SampleLib project.");
    }

    // ── format-verify-solution-wide: new format_check tool ──

    [TestMethod]
    public async Task FormatCheck_ReturnsEnvelopeWithRequiredFields()
    {
        var json = await RefactoringTools.FormatCheck(
            WorkspaceExecutionGate, FormatVerifyService, _workspaceId,
            projectName: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("checkedDocuments", out var checkedDocs));
        Assert.IsTrue(checkedDocs.GetInt32() > 0, "Expected at least one document checked in SampleSolution.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("violationCount", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("violations", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("elapsedMs", out _));
    }

    // Note: metadataName member-resolution coverage lives in SymbolResolverRenameCaretTests;
    // direct service-level test is sufficient since most MCP tools route through
    // SymbolLocatorFactory without exposing metadataName directly.
}
