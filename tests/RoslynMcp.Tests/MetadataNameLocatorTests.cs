using System.Text.Json;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.Helpers;

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
[DoNotParallelize]
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
            server: null!,
            WorkspaceManager,
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
            WorkspaceManager,
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
            server: null!,
            WorkspaceManager,
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
    public async Task CallersCallees_Accepts_Fully_Qualified_Method_Signature_With_Parameter_Types()
    {
        // gh #616 / `callers-callees-rejects-fully-qualified-names`: the canonical agent flow holds
        // a fully-qualified method signature from a sibling tool (e.g. an XML doc reference, an audit
        // report) and pastes it into `metadataName`. Before the fix the dot inside the parameter list
        // defeated the last-dot split in `ResolveByMetadataNameAsync`, producing NotFound. The
        // signature-aware resolver path now strips the parameter list and picks the matching overload.
        var json = await AnalysisTools.GetCallersCallees(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            metadataName: "SampleLib.AnimalService.CountAnimals(System.Collections.Generic.List<SampleLib.IAnimal>)",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"callers_callees should accept a fully-qualified signature. Got: {json}");

        // Resolved symbol should be the `(List<IAnimal>)` overload, not the `(IEnumerable<IAnimal>)`
        // one — proves signature matching picked the right overload rather than just the first match.
        Assert.IsTrue(doc.RootElement.TryGetProperty("symbol", out var symbol),
            "Expected `symbol` field in callers_callees response.");
        Assert.AreEqual("CountAnimals", symbol.GetProperty("name").GetString());
        var parameters = symbol.GetProperty("parameters");
        Assert.AreEqual(1, parameters.GetArrayLength(), "Expected exactly one parameter on the resolved overload.");
        var firstParam = parameters[0].GetString() ?? string.Empty;
        Assert.IsTrue(firstParam.Contains("List", StringComparison.Ordinal),
            $"Expected resolved overload's parameter to be a List<IAnimal>. Got: '{firstParam}'.");
    }

    [TestMethod]
    public async Task SignatureHelp_Accepts_Fully_Qualified_Method_Signature_With_Parameter_Types()
    {
        // gh #747 / `symbol-signature-help-returns-bare-null-for-resolvable-method-metadata`:
        // identical failure mode to gh #616 but on a sibling tool. `symbol_signature_help`
        // returned bare `null` when the supplied `metadataName` contained a parenthesized
        // parameter list because `ResolveByMetadataNameAsync` splits on the LAST dot —
        // which lands inside the parameter list — producing a bogus containing-type name.
        // The qualified-signature fallback mirrors the fix applied to `callers_callees`.
        var json = await SymbolTools.GetSignatureHelp(
            WorkspaceExecutionGate,
            SymbolRelationshipService,
            WorkspaceId,
            metadataName: "SampleLib.AnimalService.CountAnimals(System.Collections.Generic.List<SampleLib.IAnimal>)",
            ct: CancellationToken.None);

        // Critical regression guard: before the fix, `symbol_signature_help` returned bare
        // `null` (serialized as the literal JSON token `null`) rather than a structured
        // SignatureHelpDto. Assert the payload is a JSON object first.
        Assert.AreNotEqual("null", json.Trim(), "symbol_signature_help returned bare null for a resolvable metadataName (regression of gh #747).");

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind,
            $"symbol_signature_help with a fully-qualified signature should return a SignatureHelpDto object. Got: {json}");
        Assert.IsFalse(
            doc.RootElement.TryGetProperty("error", out _),
            $"symbol_signature_help should accept a fully-qualified signature. Got: {json}");

        // Validate the resolved overload's shape — proves signature matching picked the
        // `List<IAnimal>` overload, not the `IEnumerable<IAnimal>` sibling.
        Assert.IsTrue(doc.RootElement.TryGetProperty("displaySignature", out var displaySignature),
            "Expected `displaySignature` field in SignatureHelpDto response.");
        var displayText = displaySignature.GetString() ?? string.Empty;
        Assert.IsTrue(displayText.Contains("CountAnimals", StringComparison.Ordinal),
            $"Expected displaySignature to reference CountAnimals. Got: '{displayText}'.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("returnType", out _),
            "Expected `returnType` field in SignatureHelpDto response.");
        Assert.IsTrue(doc.RootElement.TryGetProperty("parameters", out var parameters),
            "Expected `parameters` field in SignatureHelpDto response.");
        Assert.AreEqual(1, parameters.GetArrayLength(),
            "Expected exactly one parameter on the resolved overload.");
        var firstParam = parameters[0].GetString() ?? string.Empty;
        Assert.IsTrue(firstParam.Contains("List", StringComparison.Ordinal),
            $"Expected resolved overload's parameter to be a List<IAnimal>. Got: '{firstParam}'.");
    }

    [TestMethod]
    public async Task FindReferences_Error_When_No_Locator_Provided()
    {
        // Preserve the legacy "no locator at all" error path. The factory's message now
        // advertises all three strategies (including metadataName) since every caller
        // supports it.
        var json = await ToolExecutionTestHarness.RunAsync(
            "find_references",
            () => SymbolTools.FindReferences(
                server: null!,
                WorkspaceManager,
                WorkspaceExecutionGate,
                ReferenceService,
                WorkspaceId,
                ct: CancellationToken.None));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(
            doc.RootElement.TryGetProperty("error", out var errorProp) && errorProp.GetBoolean(),
            $"Expected structured error envelope when no locator is provided. Got: {json}");

        Assert.IsTrue(
            doc.RootElement.GetProperty("message").GetString()!.Contains("metadataName"),
            "Error message should now advertise metadataName as a valid strategy.");
    }
}
