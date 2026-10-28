using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Resources;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// tool-alias-deprecation-stale-removal-major: the four callable aliases
/// (<c>get_symbol_outline</c>, <c>find_duplicated_code</c>, <c>get_test_coverage_map</c>,
/// <c>apply_composite_preview</c>) were removed at 5.0.0 together with the alias-lifecycle
/// (<c>deprecation</c>) metadata that only existed to describe them. These tests pin that the
/// aliases stay gone from every discovery surface and that the canonical responses no longer carry
/// the always-null <c>deprecation</c> field.
/// </summary>
[TestClass]
public sealed class AliasRemovalTests : SharedWorkspaceTestBase
{
    private static readonly string[] RemovedAliases =
    [
        "get_symbol_outline",
        "find_duplicated_code",
        "get_test_coverage_map",
        "apply_composite_preview",
    ];

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
    public void RemovedAliases_AreNotRegisteredAsMcpTools()
    {
        var registered = typeof(SymbolTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var alias in RemovedAliases)
        {
            Assert.IsFalse(registered.Contains(alias), $"{alias} must not be a registered [McpServerTool].");
        }

        var catalogNames = ServerSurfaceCatalog.Tools.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in RemovedAliases)
        {
            Assert.IsFalse(catalogNames.Contains(alias), $"{alias} must not be in the server surface catalog.");
        }

        foreach (var canonical in new[] { "document_symbols", "find_duplicated_methods", "test_coverage", "apply_composite" })
        {
            Assert.IsTrue(registered.Contains(canonical), $"{canonical} must stay registered.");
            Assert.IsTrue(catalogNames.Contains(canonical), $"{canonical} must stay in the catalog.");
        }
    }

    [TestMethod]
    public void ServerCatalogFull_PublishesNoDeprecationMetadata()
    {
        using var document = JsonDocument.Parse(ServerResources.GetServerCatalogFull());
        foreach (var entry in document.RootElement.GetProperty("tools").EnumerateArray())
        {
            Assert.IsFalse(
                entry.TryGetProperty("deprecation", out _),
                $"{entry.GetProperty("name").GetString()} must not publish alias-lifecycle metadata.");
        }
    }

    [TestMethod]
    public async Task DocumentSymbols_ResponseCarriesNoDeprecationField()
    {
        var json = await SymbolTools.GetDocumentSymbols(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: FindDocumentPath("Dog.cs"),
            ct: CancellationToken.None);

        AssertNoDeprecationField(json);
    }

    [TestMethod]
    public async Task FindDuplicatedMethods_ResponseCarriesNoDeprecationField()
    {
        var json = await AdvancedAnalysisTools.FindDuplicatedMethods(
            gate: WorkspaceExecutionGate,
            duplicateMethodDetectorService: DuplicateMethodDetectorService,
            workspaceId: WorkspaceId,
            minLines: 10,
            similarityThreshold: 0.85,
            projectFilter: null,
            limit: 50,
            ct: CancellationToken.None);

        AssertNoDeprecationField(json);
    }

    [TestMethod]
    public async Task TestCoverage_ResponseCarriesNoDeprecationField()
    {
        // The sample test project does not reference coverlet.collector, so the call hits the
        // CoverletMissing short-circuit before `dotnet test` and stays fast and deterministic.
        var json = await TestCoverageTools.RunTestCoverage(
            gate: WorkspaceExecutionGate,
            workspace: WorkspaceManager,
            commandExecutor: GatedCommandExecutor,
            options: new ValidationServiceOptions(),
            workspaceId: WorkspaceId,
            projectName: "SampleLib.Tests",
            progress: null,
            ct: CancellationToken.None);

        AssertNoDeprecationField(json);
        using var document = JsonDocument.Parse(json);
        CollectionAssert.AreEqual(
            new[] { "success", "error", "lineCoveragePercent", "branchCoveragePercent", "modules", "failureEnvelope", "coverageGaps" },
            document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray(),
            "test_coverage must serialize exactly the TestCoverageResultDto fields, in order.");
    }

    private static void AssertNoDeprecationField(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(
            document.RootElement.TryGetProperty("deprecation", out _),
            "The response must not carry an alias-lifecycle `deprecation` field.");
    }
}
