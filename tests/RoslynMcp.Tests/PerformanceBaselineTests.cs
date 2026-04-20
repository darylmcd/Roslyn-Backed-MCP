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
