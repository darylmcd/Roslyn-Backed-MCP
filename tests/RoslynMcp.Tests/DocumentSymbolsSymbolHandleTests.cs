using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

/// <summary>
/// document-symbols-accepts-symbol-handle: verifies that <c>document_symbols</c> accepts a
/// <c>symbolHandle</c> (or <c>metadataName</c>) in place of a <c>filePath</c>, returning the same
/// outline as the equivalent filePath-driven invocation.
/// </summary>
[TestClass]
public sealed class DocumentSymbolsSymbolHandleTests : SharedWorkspaceTestBase
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
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    [TestMethod]
    public async Task DocumentSymbols_AcceptsSymbolHandle_ReturnsMatchingOutline()
    {
        // Obtain a symbolHandle for AnimalService using symbol_info.
        var symbolInfoJson = await SymbolTools.GetSymbolInfo(
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            metadataName: "SampleLib.AnimalService",
            ct: CancellationToken.None);

        using var symbolInfoDoc = JsonDocument.Parse(symbolInfoJson);
        var symbolHandle = symbolInfoDoc.RootElement.GetProperty("symbolHandle").GetString();
        Assert.IsNotNull(symbolHandle, "symbol_info must return a non-null symbolHandle for AnimalService.");

        // Call document_symbols with the symbolHandle (no filePath).
        var handleJson = await SymbolTools.GetDocumentSymbols(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            symbolHandle: symbolHandle,
            ct: CancellationToken.None);

        using var handleDoc = JsonDocument.Parse(handleJson);
        Assert.IsTrue(handleDoc.RootElement.TryGetProperty("count", out var countProp),
            "document_symbols response must include a 'count' field.");
        var count = countProp.GetInt32();
        Assert.IsTrue(count > 0,
            $"document_symbols(symbolHandle) must return at least one symbol; got count={count}.");

        // Call document_symbols with the filePath for the same type and assert same count.
        var animalServicePath = FindDocumentPath("AnimalService.cs");
        var filePathJson = await SymbolTools.GetDocumentSymbols(
            server: await GetPathAuthorizedServerAsync(),
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            filePath: animalServicePath,
            ct: CancellationToken.None);

        using var filePathDoc = JsonDocument.Parse(filePathJson);
        var filePathCount = filePathDoc.RootElement.GetProperty("count").GetInt32();
        Assert.AreEqual(filePathCount, count,
            $"document_symbols(symbolHandle) and document_symbols(filePath) must return the same symbol count for AnimalService.");
    }

    [TestMethod]
    public async Task DocumentSymbols_AcceptsMetadataName_ReturnsNonEmptyOutline()
    {
        var json = await SymbolTools.GetDocumentSymbols(
            server: null!,
            gate: WorkspaceExecutionGate,
            symbolSearchService: SymbolSearchService,
            workspaceId: WorkspaceId,
            metadataName: "SampleLib.AnimalService",
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("count", out var countProp),
            "document_symbols response must include a 'count' field.");
        Assert.IsTrue(countProp.GetInt32() > 0,
            "document_symbols(metadataName) must return at least one symbol for AnimalService.");
    }
}
