using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies that <c>apply_text_edit</c> and <c>apply_multi_file_edit</c> participate
/// in the undo stack: a single pre-apply snapshot per call (single-slot per workspace),
/// fully revertible via <c>revert_last_apply</c>.
/// </summary>
[TestClass]
public sealed class EditUndoIntegrationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task ApplyTextEdit_ThenRevert_RestoresOriginalContent()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        // Append a comment line at the very end of the file via a single text edit.
        var lines = originalText.Split('\n');
        var lastLine = lines.Length;
        var lastColumn = lines[^1].Length + 1;
        var edit = new TextEditDto(lastLine, lastColumn, lastLine, lastColumn, "\n// inserted by test");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, CancellationToken.None);
        Assert.IsTrue(result.Success, "ApplyTextEditsAsync should report success.");

        var afterEdit = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterEdit, "// inserted by test");

        // Snapshot must be present in the undo stack.
        var entry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(entry, "Undo entry must exist after apply_text_edit.");
        StringAssert.Contains(entry.Description, "Apply text edit");

        // Revert and verify byte-for-byte restore.
        var reverted = await UndoService.RevertAsync(workspaceId, WorkspaceManager, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed.");

        var afterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, afterRevert,
            "After revert, Dog.cs must match the original byte-for-byte.");
    }

    [TestMethod]
    public async Task ApplyMultiFileEdit_ThenRevert_RestoresAllFiles()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var originalDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var originalCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        var dogEdit = AppendCommentEdit(originalDog, "// dog test");
        var catEdit = AppendCommentEdit(originalCat, "// cat test");

        var fileEdits = new[]
        {
            new FileEditsDto(dogFilePath, new[] { dogEdit }),
            new FileEditsDto(catFilePath, new[] { catEdit }),
        };

        var dto = await EditService.ApplyMultiFileTextEditsAsync(workspaceId, fileEdits, CancellationToken.None);
        Assert.IsTrue(dto.Success);
        Assert.AreEqual(2, dto.FilesModified);

        var afterDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var afterCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        StringAssert.Contains(afterDog, "// dog test");
        StringAssert.Contains(afterCat, "// cat test");

        // The single batch snapshot should describe a multi-file apply.
        var entry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(entry);
        StringAssert.Contains(entry.Description, "2 file(s)");

        var reverted = await UndoService.RevertAsync(workspaceId, WorkspaceManager, CancellationToken.None);
        Assert.IsTrue(reverted, "Multi-file revert should succeed.");

        var revertedDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var revertedCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        Assert.AreEqual(originalDog, revertedDog, "Dog.cs must match the original after revert.");
        Assert.AreEqual(originalCat, revertedCat, "Cat.cs must match the original after revert.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_SnapshotOverwritesPreviousRefactoring()
    {
        // Document single-slot semantics: apply rename, then apply text edit, then revert
        // → only the text edit is reverted (the rename stays). Asserts that text edits
        // overwrite the rename's snapshot like any other apply operation.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        // Apply a rename first (this sets the undo slot to the pre-rename state).
        var locator = SymbolLocator.ByMetadataName("SampleLib.IAnimal");
        var preview = await RefactoringService.PreviewRenameAsync(
            workspaceId, locator, "ICreature", CancellationToken.None);
        var renameApply = await RefactoringService.ApplyRefactoringAsync(
            preview.PreviewToken, CancellationToken.None);
        Assert.IsTrue(renameApply.Success, renameApply.Error);

        // Capture the post-rename file state — the text edit's snapshot should restore THIS,
        // not the pre-rename state.
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var postRenameDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(postRenameDog, "ICreature",
            "Sanity check: the rename should have changed Dog.cs's references.");

        // Now apply a text edit. This overwrites the undo slot with a snapshot of the
        // POST-RENAME state.
        var edit = AppendCommentEdit(postRenameDog, "// after rename");
        var editResult = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, CancellationToken.None);
        Assert.IsTrue(editResult.Success);

        // Revert: should restore the post-rename state (rename is preserved).
        var reverted = await UndoService.RevertAsync(workspaceId, WorkspaceManager, CancellationToken.None);
        Assert.IsTrue(reverted);

        var afterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(postRenameDog, afterRevert,
            "Revert should restore the snapshot taken before the text edit (i.e., post-rename state).");
        StringAssert.Contains(afterRevert, "ICreature",
            "Rename must still be in effect after the text edit was reverted.");
        Assert.IsFalse(afterRevert.Contains("// after rename", StringComparison.Ordinal),
            "Text edit comment should be gone after revert.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_RegistersUndoEntry()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        // Before any apply, no undo entry should exist.
        Assert.IsNull(UndoService.GetLastOperation(workspaceId));

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var dogText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var edit = AppendCommentEdit(dogText, "// snapshot probe");

        await EditService.ApplyTextEditsAsync(workspaceId, dogFilePath, new[] { edit }, CancellationToken.None);

        var entry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(entry, "apply_text_edit must register an undo entry.");
        StringAssert.Contains(entry.Description, "Dog.cs");
    }

    private static TextEditDto AppendCommentEdit(string fileText, string commentLine)
    {
        var lines = fileText.Split('\n');
        var lastLine = lines.Length;
        var lastColumn = lines[^1].Length + 1;
        return new TextEditDto(lastLine, lastColumn, lastLine, lastColumn, "\n" + commentLine);
    }
}
