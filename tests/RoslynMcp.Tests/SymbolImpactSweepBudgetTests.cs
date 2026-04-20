using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `symbol-impact-sweep-output-size-blowup` (P3): Jellyfin stress test
/// 2026-04-15 §4b reproduced `symbol_impact_sweep` on `BaseItem` (1452 refs) returning
/// ~886 KB and exceeding the MCP output cap on the first parallel call. Two new
/// budgets: `summary=true` drops per-ref preview text; `maxItemsPerCategory` caps
/// each list independently.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class SymbolImpactSweepBudgetTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static RoslynMcp.Roslyn.Services.ImpactSweepService _impactSweepService = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        _impactSweepService = new RoslynMcp.Roslyn.Services.ImpactSweepService(
            WorkspaceManager,
            ReferenceService,
            DiagnosticService);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task SweepAsync_DefaultMode_PopulatesPreviewText()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc);

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);
        var result = await _impactSweepService.SweepAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.References.Count >= 1, "expected at least one reference to IAnimal");
        Assert.IsTrue(result.References.Any(r => !string.IsNullOrEmpty(r.PreviewText)),
            "default summary=false must populate PreviewText on at least one reference");
    }

    [TestMethod]
    public async Task SweepAsync_SummaryMode_DropsPreviewTextOnReferencesAndMapperCallsites()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc);

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);
        var result = await _impactSweepService.SweepAsync(
            WorkspaceId, locator, CancellationToken.None, summary: true);

        Assert.IsNotNull(result);
        foreach (var loc in result.References)
        {
            Assert.IsNull(loc.PreviewText, $"summary=true must produce null PreviewText for refs (got '{loc.PreviewText}')");
            Assert.IsFalse(string.IsNullOrWhiteSpace(loc.FilePath), "FilePath must remain populated in summary mode");
        }
        foreach (var loc in result.MapperCallsites)
        {
            Assert.IsNull(loc.PreviewText, $"summary=true must produce null PreviewText for mapper callsites");
        }
    }

    [TestMethod]
    public async Task SweepAsync_MaxItemsPerCategory_CapsListIndependently()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc);

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);

        var unboundedResult = await _impactSweepService.SweepAsync(WorkspaceId, locator, CancellationToken.None);
        var cappedResult = await _impactSweepService.SweepAsync(
            WorkspaceId, locator, CancellationToken.None, summary: false, maxItemsPerCategory: 1);

        Assert.IsTrue(cappedResult.References.Count <= 1, "References must be capped to 1");
        Assert.IsTrue(cappedResult.MapperCallsites.Count <= 1, "MapperCallsites must be capped to 1");
        Assert.IsTrue(cappedResult.SwitchExhaustivenessIssues.Count <= 1, "Diagnostics must be capped to 1");

        // The cap is per-category — even when one category is at its cap, others can still
        // populate up to their own cap.
        Assert.IsTrue(unboundedResult.References.Count >= cappedResult.References.Count);
    }
}
