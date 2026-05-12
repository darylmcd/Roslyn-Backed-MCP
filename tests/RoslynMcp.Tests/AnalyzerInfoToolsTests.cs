using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for non-deterministic <c>totalRules</c> in <c>list_analyzers</c>.
///
/// Root cause (list-analyzers-totalrules-variance): the service-layer deduplication guard
/// exited early on the first project to reference an analyzer assembly, discarding rules
/// visible only from other projects' language contexts. The fix accumulates rules from all
/// projects before the DistinctBy step runs.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AnalyzerInfoToolsTests : SharedWorkspaceTestBase
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

    /// <summary>
    /// Back-to-back calls against the same workspace must return identical <c>totalRules</c>.
    /// The original early-exit guard caused project-iteration-order sensitivity that could
    /// change the count between calls in different session states.
    /// </summary>
    [TestMethod]
    public async Task ListAnalyzers_TotalRules_IsIdempotentAcrossBackToBackCalls()
    {
        var first = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);
        var second = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);

        var firstTotal = first.Sum(a => a.Rules.Count);
        var secondTotal = second.Sum(a => a.Rules.Count);

        Assert.AreEqual(firstTotal, secondTotal,
            $"totalRules must be identical on back-to-back calls. First={firstTotal}, Second={secondTotal}.");
    }

    /// <summary>
    /// The sorted rule ID sets from two back-to-back calls must be identical — same IDs,
    /// same order — proving the result is session-stable, not just numerically equal.
    /// </summary>
    [TestMethod]
    public async Task ListAnalyzers_RuleIdSets_AreIdenticalAcrossBackToBackCalls()
    {
        var first = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);
        var second = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);

        var firstIds = first
            .SelectMany(a => a.Rules.Select(r => $"{a.AssemblyName}::{r.Id}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var secondIds = second
            .SelectMany(a => a.Rules.Select(r => $"{a.AssemblyName}::{r.Id}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(firstIds, secondIds,
            "The sorted rule ID sets must be identical across back-to-back calls.");
    }

    /// <summary>
    /// An unfiltered call must return at least as many rules as any project-filtered call,
    /// since the unfiltered set is a superset of any filtered subset.
    /// </summary>
    [TestMethod]
    public async Task ListAnalyzers_UnfilteredTotalRules_AtLeastAsLargeAsAnyFilteredCall()
    {
        var unfiltered = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);
        var unfilteredTotal = unfiltered.Sum(a => a.Rules.Count);

        // Pick the first project that exists in the fixture solution for the filtered call.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var firstProjectName = solution.Projects.FirstOrDefault()?.Name;

        if (firstProjectName is null)
        {
            Assert.Inconclusive("Test fixture has no projects — cannot exercise the filtered path.");
            return;
        }

        var filtered = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: firstProjectName, CancellationToken.None);
        var filteredTotal = filtered.Sum(a => a.Rules.Count);

        Assert.IsTrue(unfilteredTotal >= filteredTotal,
            $"Unfiltered totalRules ({unfilteredTotal}) must be >= filtered totalRules ({filteredTotal}) for project '{firstProjectName}'.");
    }
}
