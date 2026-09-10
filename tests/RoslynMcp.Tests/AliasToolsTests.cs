using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// roslyn-mcp-sister-tool-name-aliases: covers the cross-MCP-server alias tools
/// (<c>get_symbol_outline</c>, <c>find_duplicated_code</c>, <c>get_test_coverage_map</c>)
/// that delegate to canonical Roslyn MCP tools (<c>document_symbols</c>,
/// <c>find_duplicated_methods</c>, <c>test_coverage</c>).
///
/// Two-part contract:
///   1. The alias's response payload (minus <c>deprecation</c>) is byte-identical to the
///      canonical's response payload (minus <c>deprecation</c>) — so callers see no
///      observable behavior difference.
///   2. The <c>deprecation</c> field is always present in the schema. It is
///      a catalog-owned lifecycle declaration on the alias and JSON <c>null</c> on the canonical.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class AliasToolsTests : SharedWorkspaceTestBase
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

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(WorkspaceId);
        return solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => document.Name == name).FilePath!;
    }

    [TestMethod]
    public async Task GetSymbolOutline_Alias_ReturnsSamePayloadAsDocumentSymbols_PlusDeprecation()
    {
        var dogFile = FindDocumentPath("Dog.cs");

        var canonicalJson = await SymbolTools.GetDocumentSymbols(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        var aliasJson = await SymbolTools.GetSymbolOutline(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        AssertAliasParity(
            canonicalJson,
            aliasJson,
            expectedAliasName: "get_symbol_outline",
            expectedCanonicalName: "document_symbols");
    }

    [TestMethod]
    public async Task FindDuplicatedCode_Alias_ReturnsSamePayloadAsFindDuplicatedMethods_PlusDeprecation()
    {
        var canonicalJson = await AdvancedAnalysisTools.FindDuplicatedMethods(
            gate: WorkspaceExecutionGate,
            duplicateMethodDetectorService: DuplicateMethodDetectorService,
            workspaceId: WorkspaceId,
            minLines: 10,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            ct: CancellationToken.None);

        var aliasJson = await AdvancedAnalysisTools.FindDuplicatedCode(
            gate: WorkspaceExecutionGate,
            duplicateMethodDetectorService: DuplicateMethodDetectorService,
            workspaceId: WorkspaceId,
            minLines: 10,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            ct: CancellationToken.None);

        AssertAliasParity(
            canonicalJson,
            aliasJson,
            expectedAliasName: "find_duplicated_code",
            expectedCanonicalName: "find_duplicated_methods");
    }

    [TestMethod]
    public async Task GetTestCoverageMap_Alias_ReturnsSamePayloadAsTestCoverage_PlusDeprecation()
    {
        // The sample test project does not reference coverlet.collector — both calls hit the
        // CoverletMissing short-circuit BEFORE invoking `dotnet test`, so this stays a fast
        // deterministic comparison instead of running the actual test suite twice.
        var canonicalJson = await TestCoverageTools.RunTestCoverage(
            gate: WorkspaceExecutionGate,
            workspace: WorkspaceManager,
            commandRunner: DotnetCommandRunner,
            workspaceId: WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            ct: CancellationToken.None);

        var aliasJson = await TestCoverageTools.GetTestCoverageMap(
            gate: WorkspaceExecutionGate,
            workspace: WorkspaceManager,
            commandRunner: DotnetCommandRunner,
            workspaceId: WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            ct: CancellationToken.None);

        AssertAliasParity(
            canonicalJson,
            aliasJson,
            expectedAliasName: "get_test_coverage_map",
            expectedCanonicalName: "test_coverage");
    }

    [TestMethod]
    public void Catalog_RegistersAllThreeAliases()
    {
        // The catalog-parity test (SurfaceCatalogTests) confirms the [McpServerTool] surface
        // matches ServerSurfaceCatalog.Tools by name. This test pins the existence + tier of
        // each alias so a refactor that drops one fails here with a focused message instead
        // of a "set differs" message buried in the parity test.
        var aliases = new[]
        {
            ("get_symbol_outline", "symbols"),
            ("find_duplicated_code", "advanced-analysis"),
            ("get_test_coverage_map", "validation"),
        };

        foreach (var (name, category) in aliases)
        {
            var entry = RoslynMcp.Host.Stdio.Catalog.ServerSurfaceCatalog.Tools
                .SingleOrDefault(t => t.Name == name);
            Assert.IsNotNull(entry, $"Alias tool '{name}' missing from ServerSurfaceCatalog.");
            Assert.AreEqual("stable", entry!.SupportTier,
                $"Alias '{name}' must be stable (matches the canonical tool's tier).");
            Assert.AreEqual(category, entry.Category,
                $"Alias '{name}' must land in the canonical tool's catalog category.");
            Assert.IsNotNull(entry.Deprecation,
                $"Alias '{name}' must publish its lifecycle declaration in the catalog.");
            Assert.AreEqual(name, entry.Deprecation.AliasName);
            Assert.AreEqual(ToolAliasDeprecation.SisterServerReason, entry.Deprecation.Reason);
            Assert.IsNull(entry.Deprecation.RiskBucket,
                "Ordinary aliases must not claim a preview/apply mutation-risk bucket.");
            Assert.AreEqual("1.33.0", entry.Deprecation.IntroducedRelease);
            Assert.AreEqual(2, entry.Deprecation.EarliestRemovalMajor);
        }
    }

    [TestMethod]
    public async Task DocumentSymbols_Canonical_ReturnsDeprecationNull()
    {
        // The canonical tool MUST emit `deprecation: null` so the response schema is
        // identical to the alias shape — clients can read deprecation unconditionally.
        var dogFile = FindDocumentPath("Dog.cs");

        var json = await SymbolTools.GetDocumentSymbols(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("deprecation", out var deprecation),
            "Canonical document_symbols must publish a `deprecation` field (even when null).");
        Assert.AreEqual(JsonValueKind.Null, deprecation.ValueKind,
            "Canonical document_symbols `deprecation` must serialize as JSON null.");
    }

    [TestMethod]
    public async Task GetSymbolOutline_Alias_DeprecationFieldShape()
    {
        // Pin every additive lifecycle field while retaining the legacy canonicalName/reason pair.
        var dogFile = FindDocumentPath("Dog.cs");

        var json = await SymbolTools.GetSymbolOutline(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("deprecation", out var deprecation));
        Assert.AreEqual(JsonValueKind.Object, deprecation.ValueKind);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "aliasName",
                "canonicalName",
                "reason",
                "riskBucket",
                "introducedRelease",
                "earliestRemovalMajor",
            },
            deprecation.EnumerateObject().Select(static property => property.Name).ToArray());
        Assert.AreEqual("get_symbol_outline", deprecation.GetProperty("aliasName").GetString());
        Assert.AreEqual("document_symbols", deprecation.GetProperty("canonicalName").GetString());
        Assert.AreEqual(
            ToolAliasDeprecation.SisterServerReason,
            deprecation.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, deprecation.GetProperty("riskBucket").ValueKind,
            "Ordinary aliases are not preview/apply routes and must not claim a risk bucket.");
        Assert.AreEqual("1.33.0", deprecation.GetProperty("introducedRelease").GetString());
        Assert.AreEqual(2, deprecation.GetProperty("earliestRemovalMajor").GetInt32());
    }

    /// <summary>
    /// Remove only <c>deprecation</c> and compare every remaining ordered field/value pair. This
    /// pins the contract that an alias is the canonical payload plus lifecycle guidance — no
    /// hidden response divergence is allowed.
    /// </summary>
    private static void AssertAliasParity(
        string canonicalJson,
        string aliasJson,
        string expectedAliasName,
        string expectedCanonicalName)
    {
        using var canonicalDoc = JsonDocument.Parse(canonicalJson);
        using var aliasDoc = JsonDocument.Parse(aliasJson);

        var canonicalWithoutDeprecation = PayloadFieldsWithoutDeprecation(canonicalDoc.RootElement);
        var aliasWithoutDeprecation = PayloadFieldsWithoutDeprecation(aliasDoc.RootElement);
        CollectionAssert.AreEqual(
            canonicalWithoutDeprecation,
            aliasWithoutDeprecation,
            "The alias response must equal the canonical response after removing only deprecation.");

        // Canonical must publish deprecation=null; alias must publish a populated envelope.
        Assert.IsTrue(canonicalDoc.RootElement.TryGetProperty("deprecation", out var canonicalDep));
        Assert.AreEqual(JsonValueKind.Null, canonicalDep.ValueKind,
            "Canonical response must emit deprecation=null.");

        Assert.IsTrue(aliasDoc.RootElement.TryGetProperty("deprecation", out var aliasDep));
        Assert.AreEqual(JsonValueKind.Object, aliasDep.ValueKind,
            "Alias response must emit a populated deprecation object.");
        Assert.AreEqual(expectedAliasName,
            aliasDep.GetProperty("aliasName").GetString());
        Assert.AreEqual(expectedCanonicalName,
            aliasDep.GetProperty("canonicalName").GetString());
        Assert.AreEqual(
            ToolAliasDeprecation.SisterServerReason,
            aliasDep.GetProperty("reason").GetString());
        Assert.AreEqual(JsonValueKind.Null, aliasDep.GetProperty("riskBucket").ValueKind);
        Assert.AreEqual("1.33.0", aliasDep.GetProperty("introducedRelease").GetString());
        Assert.AreEqual(2, aliasDep.GetProperty("earliestRemovalMajor").GetInt32());
    }

    private static string[] PayloadFieldsWithoutDeprecation(JsonElement payload) =>
        payload.EnumerateObject()
            .Where(static property => property.Name != "deprecation")
            .Select(static property => $"{property.Name}\u001f{property.Value.GetRawText()}")
            .ToArray();
}
