using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>find-overrides-payload-overflow-on-corlib-virtual</c> (gh #754):
/// when <c>find_overrides</c> resolves to a corlib virtual (e.g. <c>System.Object.ToString</c>,
/// <c>Equals</c>, <c>GetHashCode</c>) — either directly via metadata-name or indirectly via
/// promotion from an override site — <see cref="Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindOverridesAsync"/>
/// would otherwise enumerate every concrete override across the solution, producing an
/// unbounded payload. The fix adds a corlib-virtual guard that mirrors the suppression pattern
/// in <c>symbol_relationships</c> (gh #757): the service returns an empty list immediately and
/// the <c>find_overrides</c> tool wrapper emits an explanatory hint. <c>member_hierarchy.overrides</c>
/// silently benefits from the same guard via its shared <c>FindOverridesAsync</c> code path.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindOverridesCorlibSuppressionTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static string EquatablePointPath { get; set; } = null!;
    private static string ShapePath { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
        var sampleRoot = Path.GetDirectoryName(SampleSolutionPath)!;
        EquatablePointPath = Path.Combine(sampleRoot, "SampleLib", "Hierarchy", "EquatablePoint.cs");
        ShapePath = Path.Combine(sampleRoot, "SampleLib", "Hierarchy", "Shape.cs");
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // -------- Service-layer suppression --------

    /// <summary>
    /// Verifies the service-level corlib guard for <c>System.Object.ToString</c>. Without the
    /// fix, <c>FindOverridesAsync</c> would enumerate every <c>ToString</c> override across
    /// every project in the loaded solution. With the fix, the post-promotion symbol's
    /// containing type is detected as a builtin <c>SpecialType</c> and an empty list is
    /// returned immediately.
    /// </summary>
    [TestMethod]
    public async Task FindOverridesAsync_ObjectToString_ReturnsEmpty()
    {
        var locator = SymbolLocator.ByMetadataName("System.Object.ToString");

        var results = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual(0, results.Count,
            "find_overrides on System.Object.ToString must be empty — pre-fix this enumerated every ToString override across the solution.");
    }

    /// <summary>
    /// Verifies the service-level corlib guard for <c>System.Object.Equals</c>. The same
    /// rationale as the <c>ToString</c> case: every type that overrides <c>Equals</c> would
    /// otherwise surface here, producing an unbounded payload.
    /// </summary>
    [TestMethod]
    public async Task FindOverridesAsync_ObjectEquals_ReturnsEmpty()
    {
        var locator = SymbolLocator.ByMetadataName("System.Object.Equals");

        var results = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual(0, results.Count,
            "find_overrides on System.Object.Equals must be empty — pre-fix this enumerated every Equals override across the solution.");
    }

    /// <summary>
    /// Verifies the service-level corlib guard for <c>System.Object.GetHashCode</c>. Same
    /// rationale as <c>ToString</c> / <c>Equals</c>.
    /// </summary>
    [TestMethod]
    public async Task FindOverridesAsync_ObjectGetHashCode_ReturnsEmpty()
    {
        var locator = SymbolLocator.ByMetadataName("System.Object.GetHashCode");

        var results = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual(0, results.Count,
            "find_overrides on System.Object.GetHashCode must be empty — pre-fix this enumerated every GetHashCode override across the solution.");
    }

    /// <summary>
    /// Source-positional anchor on the corlib-override site itself. <c>EquatablePoint.Equals(object?)</c>
    /// at line 25 of <c>EquatablePoint.cs</c> overrides <c>System.Object.Equals(object?)</c>. After
    /// the standard <c>PromoteToVirtualRoot</c> walks up to <c>System.Object.Equals</c>, the
    /// corlib guard fires and suppresses enumeration. This guarantees the suppression behaves
    /// identically whether the caller anchors on the corlib virtual via metadata name OR on an
    /// override site that promotes to corlib.
    /// </summary>
    [TestMethod]
    public async Task FindOverridesAsync_PositionalAnchorOnCorlibOverride_ReturnsEmpty()
    {
        // Line 25, col 30 lands on "Equals" of `public override bool Equals(object? obj) => ...`.
        var locator = SymbolLocator.BySource(EquatablePointPath, line: 25, column: 30);

        var results = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.AreEqual(0, results.Count,
            "find_overrides on EquatablePoint.Equals(object?) must promote to System.Object.Equals and then suppress — pre-fix this enumerated every override across the solution.");
    }

    /// <summary>
    /// Regression guard: the corlib suppression MUST NOT fire on non-corlib virtuals. Anchoring
    /// on <c>Shape.Describe</c> (a <c>virtual</c> method declared in user code) must continue to
    /// return the expected override chain (<c>Rectangle.Describe</c>). This is the test (c) the
    /// plan calls out: "verifies suppression only fires after promotion" — promotion lands on a
    /// user-declared virtual whose containing type has <c>SpecialType == None</c>, so the
    /// guard's <c>IsCorlibVirtualMember</c> check returns <c>false</c>.
    /// </summary>
    [TestMethod]
    public async Task FindOverridesAsync_NonCorlibVirtual_ReturnsNonEmptyChain()
    {
        // Line 7, col 27 lands on "Describe" of `public virtual string Describe() => ...`. The
        // same anchor is used by MemberHierarchyCrossToolConsistencyTests.VirtualOverrideChain_*.
        var locator = SymbolLocator.BySource(ShapePath, line: 7, column: 27);

        var results = await ReferenceService.FindOverridesAsync(
            WorkspaceId, locator, CancellationToken.None);

        Assert.IsTrue(results.Count > 0,
            "find_overrides on the non-corlib virtual Shape.Describe must surface concrete overrides — the corlib guard must NOT short-circuit user-declared virtuals.");
        Assert.IsTrue(
            results.Any(symbol => symbol.FilePath?.EndsWith("Rectangle.cs", StringComparison.OrdinalIgnoreCase) == true),
            "Rectangle.Describe should remain in the overrides list for Shape.Describe.");
    }

    // -------- Wrapper-layer hint emission --------

    /// <summary>
    /// The <c>find_overrides</c> tool wrapper detects the corlib path BEFORE calling the service
    /// (the service still has the guard as defense-in-depth) and emits an explanatory
    /// <c>hint</c> field in the JSON envelope. Verifies the hint is present, points at the
    /// suppression reason, and that <c>count</c> / <c>items</c> are zero / empty so callers can
    /// programmatically detect the suppression case.
    /// </summary>
    [TestMethod]
    public async Task FindOverrides_Wrapper_CorlibMetadataName_EmitsHint()
    {
        var json = await SymbolTools.FindOverrides(
            WorkspaceExecutionGate,
            WorkspaceManager,
            ReferenceService,
            WorkspaceId,
            metadataName: "System.Object.ToString",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("count", out var count), $"Envelope must include count. Got: {json}");
        Assert.AreEqual(0, count.GetInt32(), "Corlib suppression envelope must report count=0.");
        Assert.IsTrue(root.TryGetProperty("items", out var items), $"Envelope must include items array. Got: {json}");
        Assert.AreEqual(0, items.GetArrayLength(), "Corlib suppression envelope must have an empty items array.");
        Assert.IsTrue(root.TryGetProperty("hint", out var hint),
            $"Corlib suppression envelope MUST include a hint field so the caller understands the empty result. Got: {json}");
        var hintText = hint.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hintText), "Hint must be non-empty.");
        Assert.IsTrue(hintText!.Contains("corlib", StringComparison.OrdinalIgnoreCase),
            $"Hint should mention the corlib suppression reason. Got: {hintText}");
    }

    /// <summary>
    /// Regression guard for the wrapper: when the locator does NOT resolve to a corlib virtual,
    /// the wrapper must call the service normally and emit the legacy <c>{ count, items }</c>
    /// envelope WITHOUT a <c>hint</c> field. Verifies the suppression code path doesn't bleed
    /// into the normal case.
    /// </summary>
    [TestMethod]
    public async Task FindOverrides_Wrapper_NonCorlibVirtual_NoHint()
    {
        var json = await SymbolTools.FindOverrides(
            WorkspaceExecutionGate,
            WorkspaceManager,
            ReferenceService,
            WorkspaceId,
            filePath: ShapePath,
            line: 7,
            column: 27,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("count", out var count), $"Envelope must include count. Got: {json}");
        Assert.IsTrue(count.GetInt32() > 0,
            "find_overrides on the non-corlib virtual Shape.Describe must surface concrete overrides — the wrapper must NOT short-circuit user-declared virtuals.");
        Assert.IsFalse(root.TryGetProperty("hint", out _),
            $"Non-corlib envelope MUST NOT include a hint field — that would indicate the suppression code path leaked into the normal case. Got: {json}");
    }
}
