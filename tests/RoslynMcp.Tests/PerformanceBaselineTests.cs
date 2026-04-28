using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Performance baseline tests that establish upper bounds for key operations.
/// These are not micro-benchmarks — they verify that operations complete within
/// a generous wall-clock budget to catch severe regressions.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class PerformanceBaselineTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task SymbolSearch_Completes_Within_Budget()
    {
        var sw = Stopwatch.StartNew();
        var results = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "Animal", null, null, null, 50, CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(results.Count > 0, "Expected results");
        Assert.IsTrue(sw.ElapsedMilliseconds < 10_000,
            $"Symbol search took {sw.ElapsedMilliseconds}ms — expected < 10s");
    }

    [TestMethod]
    public async Task FindReferences_Completes_Within_Budget()
    {
        var sw = Stopwatch.StartNew();
        var results = await ReferenceService.FindReferencesAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.IAnimal"),
            CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(results.Count > 0, "Expected references");
        Assert.IsTrue(sw.ElapsedMilliseconds < 10_000,
            $"Find references took {sw.ElapsedMilliseconds}ms — expected < 10s");
    }

    [TestMethod]
    public async Task ComplexityMetrics_Completes_Within_Budget()
    {
        var sw = Stopwatch.StartNew();
        var results = await CodeMetricsService.GetComplexityMetricsAsync(
            WorkspaceId, filePath: null, filePaths: null, projectFilter: null, minComplexity: null, limit: 100, CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(results.Count > 0, "Expected metrics");
        Assert.IsTrue(sw.ElapsedMilliseconds < 15_000,
            $"Complexity metrics took {sw.ElapsedMilliseconds}ms — expected < 15s");
    }

    [TestMethod]
    public async Task CouplingMetrics_PerProject_Completes_Within_Budget()
    {
        // Per-project scope (projectFilter set) MUST stay well under the 15s solution-scan budget.
        // The 8s upper bound (~50% of the solution budget) gives us regression-detection headroom
        // without flaking on shared CI runners — bumped from a tighter target after observing
        // 12807ms p50 on a 7-project / 571-doc solution during the 2026-04-27 self-audit
        // (initiative coupling-metrics-p50-borderline-on-15s-solution-budget).
        var sw = Stopwatch.StartNew();
        var results = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId,
            projectFilter: "SampleLib",
            limit: 100,
            excludeTestProjects: false,
            includeInterfaces: false,
            CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(results.Count > 0, "Expected at least one type with coupling metrics for SampleLib.");
        Assert.IsTrue(sw.ElapsedMilliseconds < 8_000,
            $"Coupling metrics (per-project) took {sw.ElapsedMilliseconds}ms — expected < 8000ms.");
    }

    [TestMethod]
    public async Task CouplingMetrics_FullSolution_Completes_Within_Budget()
    {
        // No projectFilter — exercises the full solution path. Generous 15s budget matches the
        // declared solution-scan budget for the get_coupling_metrics tool.
        var sw = Stopwatch.StartNew();
        var results = await CouplingAnalysisService.GetCouplingMetricsAsync(
            WorkspaceId,
            projectFilter: null,
            limit: 100,
            excludeTestProjects: false,
            includeInterfaces: false,
            CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(results.Count > 0, "Expected at least one type with coupling metrics for the full solution.");
        Assert.IsTrue(sw.ElapsedMilliseconds < 15_000,
            $"Coupling metrics (full solution) took {sw.ElapsedMilliseconds}ms — expected < 15000ms.");
    }

    [TestMethod]
    public async Task UnusedSymbolScan_Completes_Within_Budget()
    {
        var sw = Stopwatch.StartNew();
        var results = await UnusedCodeAnalyzer.FindUnusedSymbolsAsync(
            WorkspaceId,
            new UnusedSymbolsAnalysisOptions { ProjectFilter = null, IncludePublic = true, Limit = 50 },
            CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 30_000,
            $"Unused symbol scan took {sw.ElapsedMilliseconds}ms — expected < 30s");
    }

    [TestMethod]
    public async Task WorkspaceLoad_Completes_Within_Budget()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;

        try
        {
            var sw = Stopwatch.StartNew();
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            sw.Stop();

            Assert.IsTrue(sw.ElapsedMilliseconds < 30_000,
                $"Workspace load took {sw.ElapsedMilliseconds}ms — expected < 30s");

            WorkspaceManager.Close(status.WorkspaceId);
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }
}
