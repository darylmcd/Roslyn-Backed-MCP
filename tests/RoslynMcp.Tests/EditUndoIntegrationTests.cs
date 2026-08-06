using System.Text;
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
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success, "ApplyTextEditsAsync should report success.");

        var afterEdit = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterEdit, "// inserted by test");

        // Snapshot must be present in the undo stack.
        var entry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(entry, "Undo entry must exist after apply_text_edit.");
        StringAssert.Contains(entry.Description, "Apply text edit");

        // Revert and verify byte-for-byte restore.
        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed.");

        var afterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, afterRevert,
            "After revert, Dog.cs must match the original byte-for-byte.");
    }

    /// <summary>
    /// Regression guard for direct-mutation-undo-byte-fidelity: <c>apply_text_edit</c>
    /// must capture a byte-exact pre-apply snapshot (via <c>FileSnapshotDto.FromExistingBytes</c>)
    /// so revert restores the original BOM/encoding exactly, not a re-encoded-as-default-UTF8
    /// approximation. Mirrors <see cref="UndoFileOperationsTests.DeleteFile_Then_Revert_Restores_Original_Bytes"/>.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ApplyTextEdit_ThenRevert_RestoresOriginalBytes(bool useUtf16)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        Encoding encoding = useUtf16
            ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var originalBytes = encoding.GetPreamble()
            .Concat(encoding.GetBytes(originalText))
            .ToArray();
        await File.WriteAllBytesAsync(dogFilePath, originalBytes, CancellationToken.None);
        await workspace.ReloadAsync(CancellationToken.None);

        var edit = AppendCommentEdit(originalText, "// inserted by test");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success, "ApplyTextEditsAsync should report success.");

        var afterApplyBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        CollectionAssert.AreNotEqual(
            originalBytes,
            afterApplyBytes,
            "Sanity check: apply must have mutated the file before revert is meaningful.");

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert should succeed.");

        CollectionAssert.AreEqual(
            originalBytes,
            await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None),
            "Restored file must exactly match the pre-apply byte sequence, including its BOM and encoding.");
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

        var dto = await EditService.ApplyMultiFileTextEditsAsync(workspaceId, fileEdits, "apply_multi_file_edit", CancellationToken.None);
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

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Multi-file revert should succeed.");

        var revertedDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var revertedCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        Assert.AreEqual(originalDog, revertedDog, "Dog.cs must match the original after revert.");
        Assert.AreEqual(originalCat, revertedCat, "Cat.cs must match the original after revert.");
    }

    /// <summary>
    /// Regression guard for direct-mutation-undo-byte-fidelity: <c>apply_multi_file_edit</c>'s
    /// batch snapshot must be byte-exact per file so revert restores each file's original
    /// BOM/encoding exactly.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ApplyMultiFileEdit_ThenRevert_RestoresOriginalBytes(bool useUtf16)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var originalDogText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var originalCatText = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        Encoding encoding = useUtf16
            ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var originalDogBytes = encoding.GetPreamble().Concat(encoding.GetBytes(originalDogText)).ToArray();
        var originalCatBytes = encoding.GetPreamble().Concat(encoding.GetBytes(originalCatText)).ToArray();
        await File.WriteAllBytesAsync(dogFilePath, originalDogBytes, CancellationToken.None);
        await File.WriteAllBytesAsync(catFilePath, originalCatBytes, CancellationToken.None);
        await workspace.ReloadAsync(CancellationToken.None);

        var dogEdit = AppendCommentEdit(originalDogText, "// dog test");
        var catEdit = AppendCommentEdit(originalCatText, "// cat test");

        var fileEdits = new[]
        {
            new FileEditsDto(dogFilePath, new[] { dogEdit }),
            new FileEditsDto(catFilePath, new[] { catEdit }),
        };

        var dto = await EditService.ApplyMultiFileTextEditsAsync(workspaceId, fileEdits, "apply_multi_file_edit", CancellationToken.None);
        Assert.IsTrue(dto.Success);

        var afterApplyDogBytes = await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None);
        var afterApplyCatBytes = await File.ReadAllBytesAsync(catFilePath, CancellationToken.None);
        CollectionAssert.AreNotEqual(
            originalDogBytes,
            afterApplyDogBytes,
            "Sanity check: apply must have mutated Dog.cs before revert is meaningful.");
        CollectionAssert.AreNotEqual(
            originalCatBytes,
            afterApplyCatBytes,
            "Sanity check: apply must have mutated Cat.cs before revert is meaningful.");

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Multi-file revert should succeed.");

        CollectionAssert.AreEqual(
            originalDogBytes,
            await File.ReadAllBytesAsync(dogFilePath, CancellationToken.None),
            "Dog.cs must be restored byte-exact, including BOM/encoding.");
        CollectionAssert.AreEqual(
            originalCatBytes,
            await File.ReadAllBytesAsync(catFilePath, CancellationToken.None),
            "Cat.cs must be restored byte-exact, including BOM/encoding.");
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
            preview.PreviewToken, "test_apply", CancellationToken.None);
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
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(editResult.Success);

        // Revert: should restore the post-rename state (rename is preserved).
        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
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

        await EditService.ApplyTextEditsAsync(workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);

        var entry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(entry, "apply_text_edit must register an undo entry.");
        StringAssert.Contains(entry.Description, "Dog.cs");
    }

    /// <summary>
    /// Regression test for dr-apply-text-edit-line-break-corruption: when an edit span
    /// ends at column 1 of a subsequent line (swallowing the line break), and the
    /// replacement text does not end with a newline, the original line break must be
    /// preserved to prevent line collapse at method/declaration boundaries.
    /// </summary>
    [TestMethod]
    public async Task ApplyTextEdit_PreservesLineBreak_WhenSpanEndsAtColumn1()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;

        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        // Find "public string Speak() => \"Woof\";" — replace the entire line including
        // the trailing newline (ending at column 1 of the NEXT line) with replacement
        // text that does NOT end with a newline. Without the fix, this collapses the
        // Speak line into the Fetch method declaration.
        var lines = originalText.Split('\n');
        int speakLineNumber = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("Speak", StringComparison.Ordinal))
            {
                speakLineNumber = i + 1; // 1-based
                break;
            }
        }
        Assert.IsTrue(speakLineNumber > 0, "Dog.cs must contain a Speak method.");

        // Span: from start of the Speak line to column 1 of the next line (includes line break)
        var edit = new TextEditDto(
            speakLineNumber, 1,
            speakLineNumber + 1, 1,
            "    public string Speak() => \"Bark\";");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success);

        var afterEdit = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterEdit, "\"Bark\"", "Replacement text should be present.");

        // The critical assertion: the Fetch method must still start on its own line.
        // Without the line-break preservation fix, "Bark\";" and "public void Fetch"
        // would be on the same line.
        var afterLines = afterEdit.Split('\n');
        var barkLine = Array.FindIndex(afterLines, l => l.Contains("Bark", StringComparison.Ordinal));
        var fetchLine = Array.FindIndex(afterLines, l => l.Contains("Fetch", StringComparison.Ordinal));
        Assert.IsTrue(barkLine >= 0 && fetchLine >= 0, "Both Bark and Fetch lines must exist.");
        Assert.IsTrue(fetchLine > barkLine,
            "Fetch method must be on a separate line after the Speak replacement — " +
            "line break at method boundary must be preserved.");
    }

    private static TextEditDto AppendCommentEdit(string fileText, string commentLine)
    {
        var lines = fileText.Split('\n');
        var lastLine = lines.Length;
        var lastColumn = lines[^1].Length + 1;
        return new TextEditDto(lastLine, lastColumn, lastLine, lastColumn, "\n" + commentLine);
    }
}
