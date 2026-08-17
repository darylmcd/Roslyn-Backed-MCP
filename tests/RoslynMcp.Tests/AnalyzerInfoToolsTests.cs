using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for non-deterministic <c>totalRules</c> in <c>list_analyzers</c>, and for
/// project-filter normalization shared via <c>ProjectFilterHelper</c>.
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

    /// <summary>
    /// A whitespace-only <c>projectFilter</c> must be treated as "no filter" (matching the
    /// semantics <c>compile_check</c> already enforced), not as a literal filter that can never
    /// match a real project name. Regression coverage for the shared root cause fixed in
    /// <c>ProjectFilterHelper.FilterProjects</c> (project-filter-helper-whitespace-normalize) —
    /// prior to the fix, a whitespace-only filter silently returned zero analyzers here.
    /// </summary>
    [TestMethod]
    public async Task ListAnalyzers_WhitespaceOnlyProjectFilter_TreatedAsNoFilter()
    {
        var unfiltered = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: null, CancellationToken.None);
        var whitespaceFiltered = await AnalyzerInfoService.ListAnalyzersAsync(WorkspaceId, projectFilter: " ", CancellationToken.None);

        var unfilteredTotal = unfiltered.Sum(a => a.Rules.Count);
        var whitespaceFilteredTotal = whitespaceFiltered.Sum(a => a.Rules.Count);

        Assert.AreEqual(unfilteredTotal, whitespaceFilteredTotal,
            $"A whitespace-only projectFilter must yield the same totalRules as no filter. Unfiltered={unfilteredTotal}, WhitespaceFiltered={whitespaceFilteredTotal}.");
        Assert.IsTrue(whitespaceFilteredTotal > 0, "Whitespace-only projectFilter must not silently return zero rules.");
    }

    [TestMethod]
    public void AnalyzerLoadFailure_ReturnsSecretSafeRuleAndCapturedDiagnostic()
    {
        const string sentinel = "SECRET-SENTINEL-C:/private/analyzers/BadAnalyzer.dll";
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);
        var service = new AnalyzerInfoService(
            null!,
            null!,
            NullLogger<AnalyzerInfoService>.Instance,
            reporter);

        IReadOnlyList<AnalyzerRuleDto> rules;
        using (RequestCorrelationContext.Begin())
        {
            rules = service.GetAnalyzerRules(
                new ThrowingAnalyzerReference(sentinel),
                LanguageNames.CSharp,
                "BadAnalyzer.dll");
        }

        Assert.HasCount(1, rules);
        Assert.AreEqual("LOAD_ERROR", rules.Single().Id);
        Assert.AreEqual("Error", rules.Single().Category);
        StringAssert.Contains(rules.Single().Title, "correlationId=");
        Assert.IsFalse(JsonSerializer.Serialize(rules).Contains(sentinel, StringComparison.Ordinal));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual("AnalyzerLoad", sink.Events.Single().Category);
        Assert.IsFalse(JsonSerializer.Serialize(sink.Events.Single()).Contains(sentinel, StringComparison.Ordinal));
    }

    private sealed class ThrowingAnalyzerReference(string sentinel) : AnalyzerReference
    {
        public override string Display => "BadAnalyzer.dll";

        public override string FullPath => sentinel;

        public override object Id => "throwing-analyzer";

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) =>
            throw new InvalidOperationException(sentinel, new IOException(sentinel));

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() =>
            throw new InvalidOperationException(sentinel, new IOException(sentinel));

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) => [];
    }
}
