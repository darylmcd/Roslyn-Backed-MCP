using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

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
///      <c>{ canonicalName, reason }</c> on the alias and JSON <c>null</c> on the canonical.
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
            server: null!,
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        var aliasJson = await SymbolTools.GetSymbolOutline(
            server: null!,
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        AssertAliasParity(
            canonicalJson,
            aliasJson,
            sharedFields: ["count", "symbols"],
            expectedCanonicalName: "document_symbols");
    }

    [TestMethod]
    public async Task FindDuplicatedCode_Alias_ReturnsSamePayloadAsFindDuplicatedMethods_PlusDeprecation()
    {
        var canonicalJson = await AdvancedAnalysisTools.FindDuplicatedMethods(
            gate: WorkspaceExecutionGate,
            duplicateMethodDetectorService: ServiceContainer.DuplicateMethodDetectorService,
            workspaceId: WorkspaceId,
            minLines: 10,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            ct: CancellationToken.None);

        var aliasJson = await AdvancedAnalysisTools.FindDuplicatedCode(
            gate: WorkspaceExecutionGate,
            duplicateMethodDetectorService: ServiceContainer.DuplicateMethodDetectorService,
            workspaceId: WorkspaceId,
            minLines: 10,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            ct: CancellationToken.None);

        AssertAliasParity(
            canonicalJson,
            aliasJson,
            sharedFields: ["count", "groups"],
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
            sharedFields: ["success", "error", "lineCoveragePercent", "branchCoveragePercent", "modules", "failureEnvelope"],
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
        }
    }

    [TestMethod]
    public async Task DocumentSymbols_Canonical_ReturnsDeprecationNull()
    {
        // The canonical tool MUST emit `deprecation: null` so the response schema is
        // identical to the alias shape — clients can read deprecation unconditionally.
        var dogFile = FindDocumentPath("Dog.cs");

        var json = await SymbolTools.GetDocumentSymbols(
            server: null!,
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
        // Pin the deprecation envelope shape: { canonicalName, reason }.
        var dogFile = FindDocumentPath("Dog.cs");

        var json = await SymbolTools.GetSymbolOutline(
            server: null!,
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: dogFile,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("deprecation", out var deprecation));
        Assert.AreEqual(JsonValueKind.Object, deprecation.ValueKind);
        Assert.AreEqual("document_symbols", deprecation.GetProperty("canonicalName").GetString());
        Assert.AreEqual(
            "alias for cross-MCP-server name compatibility",
            deprecation.GetProperty("reason").GetString());
    }

    /// <summary>
    /// Project the canonical and alias payloads onto only the shared (non-deprecation) fields
    /// and assert the JSON is byte-equal. This pins the contract that "the alias is the
    /// canonical plus a deprecation envelope" — no semantic divergence is allowed.
    /// </summary>
    private static void AssertAliasParity(
        string canonicalJson,
        string aliasJson,
        string[] sharedFields,
        string expectedCanonicalName)
    {
        using var canonicalDoc = JsonDocument.Parse(canonicalJson);
        using var aliasDoc = JsonDocument.Parse(aliasJson);

        // Field-by-field equality on the shared subset. Comparing rendered RawText avoids
        // floating-point/format jitter and matches what the wire delivers.
        foreach (var field in sharedFields)
        {
            Assert.IsTrue(canonicalDoc.RootElement.TryGetProperty(field, out var canonicalField),
                $"Canonical response missing expected field '{field}'.");
            Assert.IsTrue(aliasDoc.RootElement.TryGetProperty(field, out var aliasField),
                $"Alias response missing expected field '{field}'.");
            Assert.AreEqual(
                canonicalField.GetRawText(),
                aliasField.GetRawText(),
                $"Field '{field}' diverges between canonical and alias responses.");
        }

        // Canonical must publish deprecation=null; alias must publish a populated envelope.
        Assert.IsTrue(canonicalDoc.RootElement.TryGetProperty("deprecation", out var canonicalDep));
        Assert.AreEqual(JsonValueKind.Null, canonicalDep.ValueKind,
            "Canonical response must emit deprecation=null.");

        Assert.IsTrue(aliasDoc.RootElement.TryGetProperty("deprecation", out var aliasDep));
        Assert.AreEqual(JsonValueKind.Object, aliasDep.ValueKind,
            "Alias response must emit a populated deprecation object.");
        Assert.AreEqual(expectedCanonicalName,
            aliasDep.GetProperty("canonicalName").GetString());
        Assert.AreEqual(
            "alias for cross-MCP-server name compatibility",
            aliasDep.GetProperty("reason").GetString());
    }

    /// <summary>
    /// The shared <see cref="TestServiceContainer"/> doesn't currently publish
    /// <see cref="DuplicateMethodDetectorService"/> (it's only consumed by the
    /// <see cref="DuplicateMethodDetectorTests"/> set, which builds its own AdhocWorkspace).
    /// Construct it locally against the existing shared <see cref="TestBase.WorkspaceManager"/>
    /// so the alias parity test can invoke the canonical tool wrapper without changing the
    /// shared infrastructure.
    /// </summary>
    private static class ServiceContainer
    {
        private static readonly Lazy<DuplicateMethodDetectorService> s_dup = new(() =>
        {
            var compilationCache = new CompilationCache(WorkspaceManager);
            return new DuplicateMethodDetectorService(
                WorkspaceManager,
                compilationCache,
                NullLogger<DuplicateMethodDetectorService>.Instance);
        });

        public static DuplicateMethodDetectorService DuplicateMethodDetectorService => s_dup.Value;
    }
}
