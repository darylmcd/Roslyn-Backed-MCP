using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies that the ChangeTracker records mutations applied through
/// RefactoringService and EditService apply paths.
/// </summary>
[TestClass]
public sealed class ChangeTrackerTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task RenameApply_RecordsChange()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        var locator = SymbolLocator.BySource(doc.FilePath!, 5, 6);
        var preview = await RefactoringService.PreviewRenameAsync(
            wsId, locator, "Doggo", CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(1, changes.Count, "Expected exactly 1 change after rename apply.");
        Assert.AreEqual("refactoring_apply", changes[0].ToolName);
        Assert.IsTrue(changes[0].AffectedFiles.Count > 0, "Rename should affect at least one file.");
    }

    [TestMethod]
    public async Task TwoApplies_OrderedBySequence()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.Clear(wsId);

        var doc = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.Name == "Dog.cs");

        // First apply: rename
        var locator = SymbolLocator.BySource(doc.FilePath!, 5, 6);
        var preview1 = await RefactoringService.PreviewRenameAsync(
            wsId, locator, "Doggo", CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview1.PreviewToken, CancellationToken.None);

        // Second apply: format
        var doc2 = WorkspaceManager.GetCurrentSolution(wsId)
            .Projects.SelectMany(p => p.Documents)
            .First(d => d.FilePath?.EndsWith("Dog.cs") == true || d.FilePath?.EndsWith("Doggo.cs") == true);

        var preview2 = await RefactoringService.PreviewFormatDocumentAsync(
            wsId, doc2.FilePath!, CancellationToken.None);
        await RefactoringService.ApplyRefactoringAsync(preview2.PreviewToken, CancellationToken.None);

        var changes = ChangeTracker.GetChanges(wsId);
        Assert.AreEqual(2, changes.Count, "Expected 2 changes.");
        Assert.IsTrue(changes[0].SequenceNumber < changes[1].SequenceNumber,
            "Changes should be ordered by sequence number.");
    }

    [TestMethod]
    public async Task ClearWorkspace_RemovesChanges()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var wsId = workspace.WorkspaceId;

        ChangeTracker.RecordChange(wsId, "test", ["file.cs"], "test_tool");
        Assert.AreEqual(1, ChangeTracker.GetChanges(wsId).Count);

        ChangeTracker.Clear(wsId);
        Assert.AreEqual(0, ChangeTracker.GetChanges(wsId).Count);
    }

    [TestMethod]
    public void EmptyWorkspace_ReturnsEmptyList()
    {
        var changes = ChangeTracker.GetChanges("nonexistent-workspace-id");
        Assert.AreEqual(0, changes.Count);
    }
}
