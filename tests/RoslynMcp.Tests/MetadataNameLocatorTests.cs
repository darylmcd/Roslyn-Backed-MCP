using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// Item #10 — regression guard for `find-references-metadataname-parameter-rejected`.
/// The resolver fully supported metadataName; the tool surface arbitrarily disabled it
/// for several tools by passing <c>supportsMetadataName: false</c>. This suite exercises
/// each newly-opened metadataName surface end-to-end against the sample workspace.
///
/// The 5th documented reproduction across audits was the canonical motivator: agents
/// holding a fully-qualified type name (from DI registrations, <c>get_symbol_outline</c>,
/// <c>find_unused_symbols</c>, etc.) had to fall back to <c>Grep</c> because the resolver
/// path was hard-disabled by the tool schema. Tests below replicate that agent flow.
/// </summary>
[TestClass]
public sealed class MetadataNameLocatorTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task FindReferences_Accepts_MetadataName_Without_Source_Position()
    {
        var json = await SymbolTools.FindReferences(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            metadataName: "SampleLib.AnimalService",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"find_references with metadataName-only should not error. Got: {json}");
        Assert.IsTrue(
            doc.RootElement.TryGetProperty("totalCount", out var totalCount),
            "Expected totalCount field in successful response.");
        Assert.IsTrue(
            totalCount.GetInt32() >= 0,
            "totalCount should be a valid count even if zero.");
    }

    [TestMethod]
    public async Task FindOverrides_Accepts_MetadataName_Without_Source_Position()
    {
        var json = await SymbolTools.FindOverrides(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            metadataName: "SampleLib.IAnimal.Speak",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"find_overrides with metadataName-only should not error. Got: {json}");
    }

    [TestMethod]
    public async Task TypeHierarchy_Accepts_MetadataName_Without_Source_Position()
    {
        // `dr-9-3-rejects-only-invocations` (SampleSolution audit §9.3) is the direct
        // motivator for this test.
        var json = await AnalysisTools.GetTypeHierarchy(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            metadataName: "SampleLib.IAnimal",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"type_hierarchy with metadataName-only should not error. Got: {json}");
    }

    [TestMethod]
    public async Task CallersCallees_Accepts_MetadataName_Without_Source_Position()
    {
        var json = await AnalysisTools.GetCallersCallees(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            metadataName: "SampleLib.AnimalService.GetAllAnimals",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"callers_callees with metadataName-only should not error. Got: {json}");
    }

    [TestMethod]
    public async Task GoToDefinition_Accepts_MetadataName_Without_Source_Position()
    {
        var json = await SymbolTools.GoToDefinition(
            WorkspaceExecutionGate,
            SymbolNavigationService,
            WorkspaceId,
            metadataName: "SampleLib.AnimalService",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"go_to_definition with metadataName-only should not error. Got: {json}");
    }

    [TestMethod]
    public async Task FindReferences_Error_When_No_Locator_Provided()
    {
        // Preserve the legacy "no locator at all" error path. The factory's message now
        // advertises all three strategies (including metadataName) since every caller
        // supports it.
        var json = await SymbolTools.FindReferences(
            WorkspaceExecutionGate,
            ReferenceService,
            WorkspaceId,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(
            doc.RootElement.TryGetProperty("error", out var errorProp) && errorProp.GetBoolean(),
            $"Expected structured error envelope when no locator is provided. Got: {json}");

        Assert.IsTrue(
            doc.RootElement.GetProperty("message").GetString()!.Contains("metadataName"),
            "Error message should now advertise metadataName as a valid strategy.");
    }
}
