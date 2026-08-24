using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>find-property-writes-metadataname-hint-mismatches-input-shape</c> (gh #758).
/// Pre-fix, the <c>find_property_writes</c> tool wrapper hardcoded "Position" / "column" in the
/// soft hint emitted when <c>resolvedSymbolKind</c> indicated a non-property symbol or a null
/// resolution — even when the caller supplied <c>metadataName</c> (no source position at all).
/// The hint must reflect the locator mode actually used so agents debug the right input field.
/// Mirrors the locator-aware branching in <see cref="SymbolLocatorFactory.FormatSymbolNotFoundMessage"/>.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class FindPropertyWritesHintLocatorShapeTests : SharedWorkspaceTestBase
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

        // Fixture: a class with a public instance field. FindPropertyWritesWithMetadataAsync
        // resolves the field symbol but rejects it (`symbol is not IPropertySymbol`), returning
        // (empty, "Field"). The wrapper's non-property branch then fires.
        var fixturePath = Path.Combine(CopiedRoot, "SampleLib", "HintLocatorShapeFixture.cs");
        await File.WriteAllTextAsync(fixturePath, """
namespace SampleLib;

public sealed class HintLocatorShapeProbe
{
    public int Counter;
}
""", CancellationToken.None);

        var status = await WorkspaceManager.LoadAsync(CopiedSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static async Task ClassCleanup() =>
        await CleanupFailureCollector.RunAsync(
            "Failed to dispose the property-write locator fixture.",
            CleanupFailureCollector.FromAction(() =>
            {
                if (WorkspaceId is not null)
                {
                    WorkspaceManager.Close(WorkspaceId);
                }
            }),
            CleanupFailureCollector.FromAction(() => DeleteDirectoryIfExists(CopiedRoot)),
            CleanupFailureCollector.FromAction(DisposeServices));

    /// <summary>
    /// metadataName + non-property resolution: caller passed only <c>metadataName</c>, the symbol
    /// resolves to a Field, the wrapper emits a hint. The hint must name <c>metadataName</c> as
    /// the locator field to verify and MUST NOT mention "position" / "column" — those refer to a
    /// locator mode the caller never used.
    /// </summary>
    [TestMethod]
    public async Task FindPropertyWrites_MetadataName_NonPropertySymbol_HintNamesMetadataNotPosition()
    {
        var json = await SymbolTools.FindPropertyWrites(
            WorkspaceExecutionGate,
            MutationAnalysisService,
            WorkspaceId,
            metadataName: "SampleLib.HintLocatorShapeProbe.Counter",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(0, root.GetProperty("count").GetInt32(),
            $"Field anchor must produce zero writes (the service rejects non-property symbols). Got: {json}");
        Assert.AreEqual("Field", root.GetProperty("resolvedSymbolKind").GetString(),
            $"resolvedSymbolKind must reflect the actual symbol kind. Got: {json}");

        Assert.IsTrue(root.TryGetProperty("hint", out var hint),
            $"Wrapper must emit a hint when resolvedSymbolKind is non-Property. Got: {json}");
        var hintText = hint.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hintText), $"Hint must be non-empty. Got: {json}");

        // The hint must name the locator mode the caller actually used.
        Assert.IsTrue(
            hintText!.Contains("metadataName", StringComparison.Ordinal),
            $"Hint must mention 'metadataName' so the caller knows which locator field to debug. Got: {hintText}");

        // Pre-fix wording leaked: "Position resolved to a Field..." / "...the column points...".
        // Both substrings point the agent at a source position they never supplied, so they must
        // be absent from the metadataName-mode hint.
        Assert.IsFalse(
            hintText.Contains("Position", StringComparison.OrdinalIgnoreCase),
            $"Hint must NOT mention 'Position' when the caller supplied metadataName. Got: {hintText}");
        Assert.IsFalse(
            hintText.Contains("column", StringComparison.OrdinalIgnoreCase),
            $"Hint must NOT mention 'column' when the caller supplied metadataName. Got: {hintText}");
    }

    /// <summary>
    /// metadataName + no resolution: caller passed a bogus <c>metadataName</c> the resolver
    /// cannot match, so the service returns (empty, null) and the wrapper's null-resolve branch
    /// fires. Same invariant: the hint must name <c>metadataName</c> and avoid "position" /
    /// "column" — the legacy "Verify the column points at the symbol identifier" wording leaked
    /// here too.
    /// </summary>
    [TestMethod]
    public async Task FindPropertyWrites_MetadataName_NoResolution_HintNamesMetadataNotPosition()
    {
        var json = await SymbolTools.FindPropertyWrites(
            WorkspaceExecutionGate,
            MutationAnalysisService,
            WorkspaceId,
            metadataName: "SampleLib.HintLocatorShapeProbe.NonExistentMember",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(0, root.GetProperty("count").GetInt32(),
            $"Unresolved metadataName must produce zero writes. Got: {json}");
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("resolvedSymbolKind").ValueKind,
            $"resolvedSymbolKind must be null when the symbol cannot be resolved. Got: {json}");

        Assert.IsTrue(root.TryGetProperty("hint", out var hint),
            $"Wrapper must emit a hint when the symbol does not resolve. Got: {json}");
        var hintText = hint.GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(hintText), $"Hint must be non-empty. Got: {json}");

        Assert.IsTrue(
            hintText!.Contains("metadataName", StringComparison.Ordinal),
            $"Null-resolve hint must mention 'metadataName' so the caller knows which locator field to debug. Got: {hintText}");

        Assert.IsFalse(
            hintText.Contains("position", StringComparison.OrdinalIgnoreCase),
            $"Null-resolve hint must NOT mention 'position' when the caller supplied metadataName. Got: {hintText}");
        Assert.IsFalse(
            hintText.Contains("column", StringComparison.OrdinalIgnoreCase),
            $"Null-resolve hint must NOT mention 'column' when the caller supplied metadataName. Got: {hintText}");
    }
}
