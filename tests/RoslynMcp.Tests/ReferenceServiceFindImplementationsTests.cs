using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// find-implementations-source-gen-partial-dedup regression coverage.
///
/// Fixture: <c>SampleLib/PartialImplFixture.cs</c> declares interface
/// <c>ISnapshotFixtureStore</c>, user-authored partial <c>SnapshotStore</c>, and sibling
/// <c>OtherStore</c>. <c>SampleLib/PartialImplFixture.Generated.g.cs</c> adds a second
/// partial declaration of <c>SnapshotStore</c> whose file path ends in <c>.g.cs</c> —
/// the same signal that <c>[LoggerMessage]</c> / <c>[GeneratedRegex]</c> outputs carry.
///
/// Without the dedup, <c>find_implementations</c> would emit <c>SnapshotStore</c> twice
/// (once per partial declaration) plus <c>OtherStore</c>, for a total of 3 entries on
/// a 2-implementation interface. The test asserts the default path reports exactly one
/// location per implementation symbol and that the opt-in restores the raw list.
/// </summary>
[DoNotParallelize]
[TestClass]
public class ReferenceServiceFindImplementationsTests : SharedWorkspaceTestBase
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
    public async Task FindImplementations_DedupesGeneratedPartials_ByDefault()
    {
        var results = await FindImplementationsOfFixtureInterfaceAsync(includeGeneratedPartials: false);

        // Two implementation symbols: SnapshotStore + OtherStore. Each should appear exactly once.
        Assert.AreEqual(2, results.Count,
            $"Expected exactly 2 locations after dedup, got {results.Count}: " +
            string.Join(", ", results.Select(r => System.IO.Path.GetFileName(r.FilePath))));

        Assert.IsFalse(
            results.Any(r => r.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)),
            "Default find_implementations must not surface generator-emitted .g.cs locations.");

        // The user-authored partial should be the one retained for SnapshotStore.
        Assert.IsTrue(
            results.Any(r => System.IO.Path.GetFileName(r.FilePath) == "PartialImplFixture.cs"),
            "User-authored partial for SnapshotStore should be present.");
    }

    [TestMethod]
    public async Task FindImplementations_IncludeGeneratedPartials_RestoresRawList()
    {
        var results = await FindImplementationsOfFixtureInterfaceAsync(includeGeneratedPartials: true);

        // With opt-in, SnapshotStore contributes both its user-authored and its .g.cs
        // partial declarations, and OtherStore contributes its single declaration — total 3.
        Assert.AreEqual(3, results.Count,
            $"Expected raw per-declaration count of 3 when includeGeneratedPartials=true, got {results.Count}.");

        Assert.IsTrue(
            results.Any(r => r.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)),
            "Opt-in must surface generator-emitted .g.cs partials.");
    }

    private static async Task<IReadOnlyList<LocationDto>> FindImplementationsOfFixtureInterfaceAsync(bool includeGeneratedPartials)
    {
        return await ReferenceService.FindImplementationsAsync(
            WorkspaceId,
            SymbolLocator.ByMetadataName("SampleLib.PartialImplFixture.ISnapshotFixtureStore"),
            CancellationToken.None,
            includeGeneratedPartials).ConfigureAwait(false);
    }
}
