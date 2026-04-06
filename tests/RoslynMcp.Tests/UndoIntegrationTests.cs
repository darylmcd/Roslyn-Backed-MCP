using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class UndoIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Revert_Last_Apply_Restores_Previous_State()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        // Find the Dog.Speak method and preview a rename
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var original = await SymbolSearchService.GetSymbolInfoAsync(workspaceId, locator, CancellationToken.None);
        Assert.IsNotNull(original);

        // Preview rename of IAnimal → ICreature
        var preview = await RefactoringService.PreviewRenameAsync(
            workspaceId,
            locator,
            "ICreature",
            CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);

        // Apply the rename
        var applyResult = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        // Verify the rename took effect
        var renamedSymbol = await SymbolSearchService.GetSymbolInfoAsync(
            workspaceId, SymbolLocator.ByMetadataName("SampleLib.ICreature"), CancellationToken.None);
        Assert.IsNotNull(renamedSymbol, "ICreature should exist after rename");

        // Verify undo entry exists
        var undoEntry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(undoEntry, "Undo entry should exist after apply");
        StringAssert.Contains(undoEntry.Description, "Rename");

        // Revert
        var reverted = await UndoService.RevertAsync(workspaceId, WorkspaceManager, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed");

        // Verify original name is back
        var restored = await SymbolSearchService.GetSymbolInfoAsync(
            workspaceId, SymbolLocator.ByMetadataName("SampleLib.IAnimal"), CancellationToken.None);
        Assert.IsNotNull(restored, "IAnimal should exist after revert");
    }

    [TestMethod]
    public async Task Revert_Without_Prior_Apply_Returns_False()
    {
        var result = await UndoService.RevertAsync("nonexistent-workspace", WorkspaceManager, CancellationToken.None);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetLastOperation_Returns_Null_Without_Apply()
    {
        var entry = UndoService.GetLastOperation("nonexistent-workspace");
        Assert.IsNull(entry);
    }
}
