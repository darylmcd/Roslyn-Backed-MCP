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
            preview.PreviewToken, "test_apply", CancellationToken.None);
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
        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed");

        // Verify original name is back
        var restored = await SymbolSearchService.GetSymbolInfoAsync(
            workspaceId, SymbolLocator.ByMetadataName("SampleLib.IAnimal"), CancellationToken.None);
        Assert.IsNotNull(restored, "IAnimal should exist after revert");
    }

    [TestMethod]
    public async Task Revert_Without_Prior_Apply_Returns_False()
    {
        var result = await UndoService.RevertAsync("nonexistent-workspace", CancellationToken.None);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void GetLastOperation_Returns_Null_Without_Apply()
    {
        var entry = UndoService.GetLastOperation("nonexistent-workspace");
        Assert.IsNull(entry);
    }

    [TestMethod]
    public async Task FLAG_9A_Revert_Last_Apply_Updates_Disk_File()
    {
        // FLAG-9A: previously, revert_last_apply returned reverted=true while the on-disk file
        // still contained the post-rename text. Pin the new behavior: after revert, the on-disk
        // source file must contain the pre-rename identifier.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var iAnimalDocPath = await FindDocumentPathAsync(workspaceId, "IAnimal");
        Assert.IsNotNull(iAnimalDocPath, "IAnimal source file path should resolve");

        var beforeRenameDisk = await File.ReadAllTextAsync(iAnimalDocPath, CancellationToken.None);
        StringAssert.Contains(beforeRenameDisk, "IAnimal");

        var preview = await RefactoringService.PreviewRenameAsync(workspaceId, locator, "ICreature", CancellationToken.None);
        Assert.IsNotNull(preview.PreviewToken);
        var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applyResult.Success, applyResult.Error);

        // After apply, disk must show the rename.
        var afterRenameDisk = await File.ReadAllTextAsync(iAnimalDocPath, CancellationToken.None);
        StringAssert.Contains(afterRenameDisk, "ICreature");

        // Now revert and verify disk has the pre-rename text again.
        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should report success");

        var afterRevertDisk = await File.ReadAllTextAsync(iAnimalDocPath, CancellationToken.None);
        StringAssert.Contains(afterRevertDisk, "IAnimal");
        Assert.IsFalse(afterRevertDisk.Contains("ICreature", StringComparison.Ordinal),
            "FLAG-9A regression: revert_last_apply must restore disk content, not just workspace state");
    }

    private static async Task<string?> FindDocumentPathAsync(string workspaceId, string typeName)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath is null) continue;
                var text = await doc.GetTextAsync().ConfigureAwait(false);
                if (text.ToString().Contains(typeName, StringComparison.Ordinal))
                {
                    return doc.FilePath;
                }
            }
        }
        return null;
    }
}
