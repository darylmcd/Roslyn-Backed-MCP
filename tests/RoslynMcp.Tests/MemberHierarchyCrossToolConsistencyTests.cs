using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Closes gh #736 (member_hierarchy.overrides mislabels sibling interface impls) and gh
/// #737 (find_overrides vs member_hierarchy cross-tool inconsistency). Both symptoms had a
/// shared root cause in <c>ReferenceService.FindOverridesAsync</c>, which conflated true
/// virtual/abstract overrides with sibling interface implementations into a single bucket.
/// </summary>
/// <remarks>
/// Post-fix invariants enforced here:
/// <list type="number">
///   <item><description><c>find_overrides</c> and <c>member_hierarchy.overrides</c> always agree
///   on the same definition of "override" — symbols actually marked <c>override</c> of a
///   virtual or abstract declaration.</description></item>
///   <item><description>Sibling interface implementations live in the new
///   <c>member_hierarchy.SiblingInterfaceImplementations</c> bucket.</description></item>
///   <item><description>The agreement holds across all locator strategies (source position
///   on the implementing method, source position on the interface declaration, and metadata
///   name on the interface member).</description></item>
/// </list>
/// </remarks>
[DoNotParallelize]
[TestClass]
public sealed class MemberHierarchyCrossToolConsistencyTests : SharedWorkspaceTestBase
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
    public async Task IDisposableDispose_OverridesEmpty_SiblingImplsPopulated_AcrossTools()
    {
        // BacklogDisposableSample.Dispose (line 12, col 17) is an implementation of the
        // IDisposable.Dispose interface contract, NOT a true `override` of any virtual
        // method. Pre-fix: find_overrides returned the impls (mislabelled), and
        // member_hierarchy.overrides matched. Post-fix: both surfaces return 0 for
        // overrides; the impls move to member_hierarchy.SiblingInterfaceImplementations.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var backlogFile = solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == "BacklogSamples.cs");

        var locatorAtImpl = SymbolLocator.BySource(backlogFile.FilePath!, 12, 17);

        var findOverridesAtImpl = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locatorAtImpl, CancellationToken.None);
        var hierarchyAtImpl = await SymbolRelationshipService.GetMemberHierarchyAsync(
            WorkspaceId, locatorAtImpl, CancellationToken.None);

        Assert.IsNotNull(hierarchyAtImpl);
        Assert.AreEqual(0, findOverridesAtImpl.Count,
            "find_overrides on BacklogDisposableSample.Dispose must be 0 — Dispose is a sibling impl, not a true override.");
        Assert.AreEqual(
            hierarchyAtImpl.Overrides.Count,
            findOverridesAtImpl.Count,
            "find_overrides and member_hierarchy.overrides must agree (both = 0 for sibling impls).");
        Assert.IsTrue(hierarchyAtImpl.SiblingInterfaceImplementations.Count > 0,
            "member_hierarchy.SiblingInterfaceImplementations should surface the IDisposable.Dispose impls (was previously bucketed under overrides).");
        Assert.IsTrue(
            hierarchyAtImpl.SiblingInterfaceImplementations.Any(symbol =>
                symbol.Name == "Dispose" &&
                symbol.FilePath?.EndsWith("BacklogSamples.cs", StringComparison.OrdinalIgnoreCase) == true),
            "BacklogDisposableSample.Dispose should appear in the SiblingInterfaceImplementations bucket.");

        // Cross-tool symmetry: querying by metadata name on IDisposable.Dispose must yield
        // the same answer as the source-positional query. This is the gh #737 surface — pre-fix
        // the metadata-name query returned 0 (silent miss in FindImplementationForInterfaceMember
        // when the symbol came from a metadata reference) while the source-positional query
        // returned the impls. Post-fix: both return 0 for overrides (correct), and the impls
        // live in the sibling-impls bucket regardless of locator strategy.
        var locatorAtMetadata = SymbolLocator.ByMetadataName("System.IDisposable.Dispose");
        var findOverridesAtMetadata = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locatorAtMetadata, CancellationToken.None);
        Assert.AreEqual(0, findOverridesAtMetadata.Count,
            "find_overrides at IDisposable.Dispose (metadata) must agree with the source-positional query at 0.");
    }

    [TestMethod]
    public async Task VirtualOverrideChain_OverridesPopulated_SiblingImplsEmpty_AcrossTools()
    {
        // Shape.Describe is a virtual method; Rectangle.Describe is a true `override`. The
        // override bucket must remain populated; the SiblingInterfaceImplementations bucket
        // must be empty because Describe is not an interface contract member.
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        var shapeFile = solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == "Shape.cs");

        var locator = SymbolLocator.BySource(shapeFile.FilePath!, 7, 27);

        var overrides = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);
        var hierarchy = await SymbolRelationshipService.GetMemberHierarchyAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.IsNotNull(hierarchy);
        Assert.IsTrue(overrides.Count > 0,
            "find_overrides on virtual Shape.Describe must surface concrete overrides (e.g. Rectangle.Describe).");
        Assert.AreEqual(
            hierarchy.Overrides.Count,
            overrides.Count,
            "find_overrides and member_hierarchy.overrides must agree on true virtual overrides.");
        Assert.IsTrue(
            hierarchy.Overrides.Any(symbol =>
                symbol.FilePath?.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase) == true),
            "Rectangle.Describe should remain in the overrides bucket (it really is an `override`).");
        Assert.AreEqual(0, hierarchy.SiblingInterfaceImplementations.Count,
            "Shape.Describe is not an interface contract member; SiblingInterfaceImplementations must be empty.");
    }
}
