using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>find-property-writes-positional-record-silent-zero</c>.
/// Before the fix <c>FindPropertyWritesAsync</c> returned zero writes for a positional-record
/// primary-ctor-bound property because <c>SymbolFinder.FindReferencesAsync(property, ...)</c>
/// attributes the construction-site bindings to the primary-ctor parameter rather than the
/// synthesized property. Each <c>new T(value)</c> call must now surface as a
/// <c>PrimaryConstructorBind</c> bucket in the result list.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindPropertyWritesPositionalRecordTests : SharedWorkspaceTestBase
{
    private static string CopiedRoot { get; set; } = null!;
    private static string CopiedSolutionPath { get; set; } = null!;
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        CopiedSolutionPath = CreateSampleSolutionCopy();
        CopiedRoot = Path.GetDirectoryName(CopiedSolutionPath)!;

        // Canonical repro shape from the backlog row: a positional record whose only write
        // sites are construction-time `new T(value)` bindings (WebSnapshotStoreContext.Store).
        // The context class exercises two separate construction sites so the test can
        // distinguish "one site only" over-fitting from real detection.
        var fixturePath = Path.Combine(CopiedRoot, "SampleLib", "PositionalRecordWriteFixture.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public record WebSnapshotStoreContext(string Store, int Port);

public static class WebSnapshotConsumer
{
    public static WebSnapshotStoreContext CreateAtSite1() =>
        new WebSnapshotStoreContext("primary", 8080);

    public static WebSnapshotStoreContext CreateAtSite2()
    {
        return new WebSnapshotStoreContext("secondary", 9090);
    }

    public static WebSnapshotStoreContext ReadOnly(WebSnapshotStoreContext existing)
    {
        // This is a READ of Store, not a write — must NOT appear in the result set.
        var s = existing.Store;
        return existing with { Port = 1111 };
    }
}
""", CancellationToken.None);

        var status = await WorkspaceManager.LoadAsync(CopiedSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (WorkspaceId is not null)
        {
            try { WorkspaceManager.Close(WorkspaceId); } catch { }
        }
        DeleteDirectoryIfExists(CopiedRoot);
        DisposeServices();
    }

    [TestMethod]
    public async Task FindPropertyWrites_PositionalRecordProperty_ReportsPrimaryConstructorBindSites()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.WebSnapshotStoreContext.Store");
        var (writes, resolvedKind) = await MutationAnalysisService
            .FindPropertyWritesWithMetadataAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual("Property", resolvedKind,
            $"WebSnapshotStoreContext.Store must resolve to a Property (the synthesized positional-record property). Got resolvedKind={resolvedKind}, writes.Count={writes.Count}");

        var primaryCtorBinds = writes.Where(w => w.WriteKind == "PrimaryConstructorBind").ToList();
        Assert.AreEqual(2, primaryCtorBinds.Count,
            "Both `new WebSnapshotStoreContext(...)` call sites must be reported as PrimaryConstructorBind writes. " +
            $"Got: {string.Join(", ", writes.Select(w => $"{w.WriteKind}@{w.StartLine}"))} (total {writes.Count})");

        // Sanity: the `existing with { Port = ... }` site writes Port, NOT Store — none of the
        // reported writes should be on `Store` from a with-expression in this fixture.
        Assert.IsFalse(writes.Any(w => w.WriteKind == "ObjectInitializer"),
            "Fixture contains no `with { Store = ... }` site — no ObjectInitializer writes expected for Store.");
    }

    [TestMethod]
    public async Task FindPropertyWrites_PositionalRecordProperty_WithExpressionOnDifferentSlot_IsReportedOnThatSlot()
    {
        var locator = SymbolLocator.ByMetadataName("SampleLib.WebSnapshotStoreContext.Port");
        var (writes, resolvedKind) = await MutationAnalysisService
            .FindPropertyWritesWithMetadataAsync(WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual("Property", resolvedKind);

        // Two ctor binds + one `with { Port = 1111 }`.
        var primaryCtorBinds = writes.Count(w => w.WriteKind == "PrimaryConstructorBind");
        var withBinds = writes.Count(w => w.WriteKind == "ObjectInitializer");

        Assert.AreEqual(2, primaryCtorBinds, "Both construction sites bind Port positionally.");
        Assert.AreEqual(1, withBinds, "The `with { Port = 1111 }` site is an ObjectInitializer write (WithInitializerExpression derives from InitializerExpressionSyntax).");
    }
}
