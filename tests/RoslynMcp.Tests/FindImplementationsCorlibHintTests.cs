using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>find-implementations-corlib-metadataname-zero</c>: when
/// <c>find_implementations</c> resolves a metadataName to a corlib/BCL interface root
/// (e.g. <c>System.IDisposable</c>), <see cref="Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindImplementationsAsync"/>
/// binds the symbol to a single project's corlib reference, so cross-project implementers
/// can silently drop out and the query reads as count 0 — indistinguishable from "no
/// implementers." Same metadata-boundary family as the find_overrides corlib-virtual guard
/// (gh #754). The fix adds a tool-layer guard: when the resolved symbol is a corlib
/// implementation root, the wrapper emits a structured <c>hint</c> ("source-anchor this query")
/// instead of a bare (possibly-empty) set. Non-corlib roots are unaffected.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindImplementationsCorlibHintTests : SharedWorkspaceTestBase
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

    // -------- Predicate-level --------

    /// <summary>
    /// The corlib-implementation-root predicate must classify the metadata <c>System.IDisposable</c>
    /// interface as a suppressed root, and must NOT classify a user-declared concrete type
    /// (<c>BacklogDisposableSample</c>) as one.
    /// </summary>
    [TestMethod]
    public async Task IsCorlibImplementationRoot_Classifies_OnlyMetadataInterfaceRoots()
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);

        var iDisposable = await SymbolResolver.ResolveAsync(
            solution, SymbolLocator.ByMetadataName("System.IDisposable"), CancellationToken.None);
        Assert.IsNotNull(iDisposable, "System.IDisposable should resolve from the corlib reference.");
        Assert.IsTrue(ReferenceService.IsCorlibImplementationRoot(iDisposable!),
            "System.IDisposable is a metadata interface root and must be classified as a corlib implementation root.");

        var userType = await SymbolResolver.ResolveAsync(
            solution, SymbolLocator.ByMetadataName("SampleLib.BacklogDisposableSample"), CancellationToken.None);
        Assert.IsNotNull(userType, "SampleLib.BacklogDisposableSample should resolve.");
        Assert.IsFalse(ReferenceService.IsCorlibImplementationRoot(userType!),
            "A user-declared concrete type must NOT be classified as a corlib implementation root.");
    }

    // -------- Wrapper-layer hint emission --------

    /// <summary>
    /// The <c>find_implementations</c> tool wrapper detects the corlib path BEFORE calling the
    /// service and emits an explanatory <c>hint</c> in the JSON envelope. Verifies the hint is
    /// present, points at the source-anchor remedy, and that <c>count</c> / <c>items</c> are
    /// zero / empty so callers can programmatically detect the suppression case — i.e. NOT a bare
    /// empty set.
    /// </summary>
    [TestMethod]
    public async Task FindImplementations_Wrapper_CorlibMetadataName_EmitsHint()
    {
        var json = await SymbolTools.FindImplementations(
            WorkspaceExecutionGate,
            WorkspaceManager,
            ReferenceService,
            WorkspaceId,
            metadataName: "System.IDisposable",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("count", out var count), $"Envelope must include count. Got: {json}");
        Assert.AreEqual(0, count.GetInt32(), "Corlib hint envelope must report count=0.");
        Assert.IsTrue(root.TryGetProperty("items", out var items), $"Envelope must include items array. Got: {json}");
        Assert.AreEqual(0, items.GetArrayLength(), "Corlib hint envelope must have an empty items array.");
        Assert.IsTrue(root.TryGetProperty("hint", out var hint),
            $"Corlib metadataName envelope MUST include a hint field so the caller understands the empty result. Got: {json}");
        var hintText = hint.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hintText), "Hint must be non-empty.");
        Assert.IsTrue(hintText!.Contains("source-anchor", StringComparison.OrdinalIgnoreCase),
            $"Hint should tell the caller to source-anchor the query. Got: {hintText}");
        Assert.IsTrue(hintText!.Contains("corlib", StringComparison.OrdinalIgnoreCase),
            $"Hint should mention the corlib suppression reason. Got: {hintText}");
    }

    /// <summary>
    /// Regression guard: when the locator resolves to a user-declared interface root (source,
    /// not corlib), the wrapper must call the service normally and emit the legacy
    /// <c>{ count, items, includeGeneratedPartials }</c> envelope WITHOUT a <c>hint</c> field.
    /// Uses the existing <c>ISnapshotFixtureStore</c> fixture, which has 2 source implementers.
    /// </summary>
    [TestMethod]
    public async Task FindImplementations_Wrapper_UserInterface_NoHint()
    {
        var json = await SymbolTools.FindImplementations(
            WorkspaceExecutionGate,
            WorkspaceManager,
            ReferenceService,
            WorkspaceId,
            metadataName: "SampleLib.PartialImplFixture.ISnapshotFixtureStore",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("count", out var count), $"Envelope must include count. Got: {json}");
        Assert.IsTrue(count.GetInt32() > 0,
            "find_implementations on the user-declared ISnapshotFixtureStore must surface concrete implementers — the corlib guard must NOT short-circuit user interfaces.");
        Assert.IsFalse(root.TryGetProperty("hint", out _),
            $"Non-corlib envelope MUST NOT include a hint field — that would indicate the suppression code path leaked into the normal case. Got: {json}");
    }
}
