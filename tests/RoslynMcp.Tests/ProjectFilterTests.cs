using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// find-references-project-filter regression coverage.
///
/// Verifies that the new <c>projectFilter</c> parameter on
/// <c>IReferenceService.FindReferencesAsync</c> and
/// <c>IConsumerAnalysisService.FindConsumersAsync</c> behaves as documented:
///
///   * Null/empty filter returns the same result set as the unfiltered (legacy) call —
///     byte-identical regression baseline.
///   * Single-project filter narrows the result to references whose document belongs to
///     that project (case-sensitive Project.Name match — matches semantic_grep semantics).
///
/// Fixture: <c>IAnimal</c> in <c>SampleLib</c> has consumers across multiple projects in
/// the SampleSolution layout (<c>SampleLib</c>, <c>SampleApp</c>, <c>SampleLib.Tests</c>),
/// so a project-name filter is observable.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class ProjectFilterTests : SharedWorkspaceTestBase
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

    [TestMethod]
    public async Task FindReferences_NullProjectFilter_MatchesUnfilteredBaseline()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");

        // Legacy two-arg-style call (no projectFilter argument): the IReadOnlyCollection<string>?
        // default of null must produce the same set as an explicit null pass.
        var baseline = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None);
        var explicitNull = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: false, projectFilter: null);
        var explicitEmpty = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None, summary: false, projectFilter: Array.Empty<string>());

        Assert.AreEqual(baseline.Count, explicitNull.Count, "explicit null projectFilter must match the legacy default");
        Assert.AreEqual(baseline.Count, explicitEmpty.Count, "empty projectFilter must match the legacy default");
        // Stable ordering is documented on the service; pairwise compare by (FilePath, line, column).
        for (var i = 0; i < baseline.Count; i++)
        {
            Assert.AreEqual(baseline[i].FilePath, explicitNull[i].FilePath);
            Assert.AreEqual(baseline[i].StartLine, explicitNull[i].StartLine);
            Assert.AreEqual(baseline[i].StartColumn, explicitNull[i].StartColumn);
        }
    }

    [TestMethod]
    public async Task FindReferences_SingleProjectFilter_NarrowsToThatProject()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");

        var unfiltered = await ReferenceService.FindReferencesAsync(WorkspaceId, locator, CancellationToken.None);
        var filtered = await ReferenceService.FindReferencesAsync(
            WorkspaceId, locator, CancellationToken.None, summary: false, projectFilter: new[] { "SampleLib" });

        Assert.IsTrue(filtered.Count > 0, "Expected at least one reference inside SampleLib");
        Assert.IsTrue(filtered.Count <= unfiltered.Count,
            $"Filtered result must be a subset (filtered={filtered.Count}, unfiltered={unfiltered.Count})");

        // Every filtered hit must live under the SampleLib project directory. We can't read
        // Project.Name back out of LocationDto directly, so anchor on the path segment that
        // every SampleLib document shares: '\\SampleLib\\' (or '/SampleLib/' on POSIX).
        foreach (var dto in filtered)
        {
            var path = dto.FilePath ?? string.Empty;
            var inSampleLib = path.Contains($"{Path.DirectorySeparatorChar}SampleLib{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains("/SampleLib/", StringComparison.Ordinal);
            Assert.IsTrue(inSampleLib, $"Filtered hit not inside SampleLib: {path}");
        }
    }

    [TestMethod]
    public async Task FindReferences_NonexistentProjectFilter_ReturnsEmpty()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var filtered = await ReferenceService.FindReferencesAsync(
            WorkspaceId, locator, CancellationToken.None, summary: false, projectFilter: new[] { "DoesNotExist" });
        Assert.AreEqual(0, filtered.Count, "filter pointing at a non-existent project name must yield zero hits");
    }

    [TestMethod]
    public async Task FindConsumers_NullProjectFilter_MatchesUnfilteredBaseline()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");

        var baseline = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);
        var explicitNull = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None, projectFilter: null);
        var explicitEmpty = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None, projectFilter: Array.Empty<string>());

        Assert.IsNotNull(baseline);
        Assert.IsNotNull(explicitNull);
        Assert.IsNotNull(explicitEmpty);
        Assert.AreEqual(baseline.Consumers.Count, explicitNull.Consumers.Count, "explicit null filter must equal legacy default");
        Assert.AreEqual(baseline.Consumers.Count, explicitEmpty.Consumers.Count, "empty filter must equal legacy default");
    }

    [TestMethod]
    public async Task FindConsumers_SingleProjectFilter_NarrowsToThatProject()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");

        var unfiltered = await ConsumerAnalysisService.FindConsumersAsync(WorkspaceId, locator, CancellationToken.None);
        Assert.IsNotNull(unfiltered);

        var filtered = await ConsumerAnalysisService.FindConsumersAsync(
            WorkspaceId, locator, CancellationToken.None, projectFilter: new[] { "SampleLib" });
        Assert.IsNotNull(filtered);

        Assert.IsTrue(filtered.Consumers.Count > 0, "Expected at least one IAnimal consumer inside SampleLib (Dog, Cat, AnimalService)");
        Assert.IsTrue(filtered.Consumers.Count <= unfiltered.Consumers.Count,
            $"Filtered consumer set must be a subset (filtered={filtered.Consumers.Count}, unfiltered={unfiltered.Consumers.Count})");

        // Every filtered consumer's containing-type Project.Name (carried on TypeConsumerDto.ProjectName)
        // must match the filter — that's the contract the wrapper exposes to MCP callers.
        foreach (var consumer in filtered.Consumers)
        {
            Assert.AreEqual("SampleLib", consumer.ProjectName,
                $"Filtered consumer leaked from non-SampleLib project: {consumer.TypeName} ({consumer.ProjectName})");
        }
    }

    [TestMethod]
    public async Task FindConsumers_NonexistentProjectFilter_ReturnsEmpty()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var filtered = await ConsumerAnalysisService.FindConsumersAsync(
            WorkspaceId, locator, CancellationToken.None, projectFilter: new[] { "DoesNotExist" });
        Assert.IsNotNull(filtered);
        Assert.AreEqual(0, filtered.Consumers.Count, "filter pointing at a non-existent project name must yield zero consumers");
    }
}
