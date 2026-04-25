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

    /// <summary>
    /// Regression for `symbol-impact-sweep-suggested-tasks-count-drift` (P4): the
    /// suggested-tasks string was previously built from the truncated list length
    /// (`references.Count` after `Take(cap)`), so a 193-ref symbol capped at 10 reported
    /// "Review 10 reference(s)" instead of the true blast radius. Reviewers cannot size the
    /// work from the capped count. The fix captures pre-cap totals before the `Take(cap)`
    /// pass and feeds those into the task-string builder.
    /// </summary>
    [TestMethod]
    public async Task SweepAsync_SuggestedTasks_ReportsPreCapReferenceTotal()
    {
        var animalDoc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.Name == "IAnimal.cs");
        Assert.IsNotNull(animalDoc);

        var locator = SymbolLocator.BySource(animalDoc.FilePath!, line: 5, column: 10);

        // Run unbounded first to capture the true total — the IAnimal sample fixture has
        // multiple references; we just need >= 2 to make the truncation observable.
        var unboundedResult = await _impactSweepService.SweepAsync(WorkspaceId, locator, CancellationToken.None);
        var totalReferenceCount = unboundedResult.References.Count;
        Assert.IsTrue(totalReferenceCount >= 2,
            $"sample fixture must have >=2 IAnimal references for this test to be meaningful (got {totalReferenceCount})");

        // Cap to 1 so the truncated list length (1) differs from the pre-cap total (>=2).
        var cappedResult = await _impactSweepService.SweepAsync(
            WorkspaceId, locator, CancellationToken.None, summary: false, maxItemsPerCategory: 1);

        Assert.AreEqual(1, cappedResult.References.Count, "guard: cap must have truncated to 1");

        // The suggested-tasks string MUST cite the pre-cap total, not the capped list length.
        var referenceTask = cappedResult.SuggestedTasks
            .FirstOrDefault(t => t.StartsWith("Review ", StringComparison.Ordinal));
        Assert.IsNotNull(referenceTask, "expected a 'Review N reference(s)' task entry");
        StringAssert.Contains(referenceTask, $"Review {totalReferenceCount} reference(s)",
            $"task-string must report pre-cap total {totalReferenceCount}, not truncated list length 1 (got: '{referenceTask}')");
    }
}
