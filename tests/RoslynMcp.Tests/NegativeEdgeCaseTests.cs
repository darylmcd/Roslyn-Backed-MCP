using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NegativeEdgeCaseTests : TestBase
{
    private static string WorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        var status = await WorkspaceManager.LoadAsync(SampleSolutionPath, CancellationToken.None);
        WorkspaceId = status.WorkspaceId;
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public void InvalidWorkspaceId_ThrowsKeyNotFoundException()
    {
        Assert.ThrowsException<KeyNotFoundException>(() =>
            WorkspaceManager.GetStatus("nonexistent-workspace-id"));
    }

    [TestMethod]
    public async Task MalformedSymbolHandle_InvalidBase64_ThrowsArgumentException()
    {
        var locator = SymbolLocator.ByHandle("not-valid-base64!!!");
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            SymbolNavigationService.GoToDefinitionAsync(WorkspaceId, locator, CancellationToken.None));
        StringAssert.Contains(ex.Message, "base64", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task MalformedSymbolHandle_ValidBase64InvalidJson_ThrowsArgumentException()
    {
        var handle = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("this is not json"));
        var locator = SymbolLocator.ByHandle(handle);
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            SymbolNavigationService.GoToDefinitionAsync(WorkspaceId, locator, CancellationToken.None));
        StringAssert.Contains(ex.Message, "JSON", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void SymbolLocator_NoFields_ThrowsArgumentException()
    {
        var locator = new SymbolLocator(null, null, null, null, null);
        Assert.ThrowsException<ArgumentException>(() => locator.Validate());
    }

    [TestMethod]
    public async Task SourceLocation_NonExistentFile_ReturnsEmpty()
    {
        var locator = SymbolLocator.BySource(@"C:\nonexistent\file.cs", 1, 1);
        var result = await SymbolNavigationService.GoToDefinitionAsync(WorkspaceId, locator, CancellationToken.None);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task NegativeLineColumn_ThrowsArgumentOutOfRange()
    {
        var status = WorkspaceManager.GetStatus(WorkspaceId);
        var project = status.Projects.First(p => !p.IsTestProject);
        var doc = WorkspaceManager.GetCurrentSolution(WorkspaceId)
            .Projects.First(p => p.Name == project.Name)
            .Documents.First(d => d.FilePath?.EndsWith(".cs") == true);

        var locator = SymbolLocator.BySource(doc.FilePath!, -1, -1);
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            SymbolNavigationService.GoToDefinitionAsync(WorkspaceId, locator, CancellationToken.None));
    }

    [TestMethod]
    public async Task EmptySearchQuery_HandledGracefully()
    {
        var result = await SymbolSearchService.SearchSymbolsAsync(
            WorkspaceId, "", null, null, null, 20, CancellationToken.None);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task PreCancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await SymbolSearchService.SearchSymbolsAsync(WorkspaceId, "Dog", null, null, null, 20, cts.Token);
            Assert.Fail("Expected OperationCanceledException or TaskCanceledException");
        }
        catch (OperationCanceledException)
        {
            // Expected — TaskCanceledException is a subclass of OperationCanceledException
        }
    }
}
