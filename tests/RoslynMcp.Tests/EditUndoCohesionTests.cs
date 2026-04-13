using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the PR 2 editing/undo cohesion fixes:
///   * <c>apply-text-edit-invalid-edit-corrupt-diff</c> — <see cref="IEditService.ApplyTextEditsAsync"/>
///     must reject malformed ranges (null NewText, out-of-bounds, reversed) BEFORE any disk write
///     or diff generation so the caller never sees a corrupt unified diff.
///   * <c>revert-last-apply-disk-consistency</c> — <see cref="IUndoService.RevertAsync"/> must restore
///     disk even when the Roslyn Solution diff is empty, using either the explicit file-snapshot fast
///     path or the disk-walk safety net in the legacy solution-based path.
///   * <c>set-editorconfig-option-not-undoable</c> — <see cref="IEditorConfigService.SetOptionAsync"/>
///     now participates in the undo stack; revert must restore the pre-write .editorconfig content
///     (or delete the file if the set operation created it).
/// </summary>
[TestClass]
public sealed class EditUndoCohesionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ------------------------------------------------------------------
    // apply-text-edit-invalid-edit-corrupt-diff
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_NullNewText_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Intentionally build a malformed edit with null NewText. TextEditDto is a positional
        // record so we construct it via `default` + expression to bypass nullable analysis.
        var edit = new TextEditDto(1, 1, 1, 1, null!);

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None));
        StringAssert.Contains(ex.Message, "null NewText");
    }

    [TestMethod]
    public async Task ApplyTextEdit_ReversedRange_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Start (5,10) → End (5,5): end precedes start on the same line.
        var edit = new TextEditDto(5, 10, 5, 5, "x");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None));
        StringAssert.Contains(ex.Message, "reversed range");
    }

    [TestMethod]
    public async Task ApplyTextEdit_OutOfBoundsLine_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Line 9999 is far beyond the file length.
        var edit = new TextEditDto(9999, 1, 9999, 1, "oops");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None));
        StringAssert.Contains(ex.Message, "9999");
    }

    [TestMethod]
    public async Task ApplyTextEdit_NonPositiveColumn_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Column 0 is invalid — line/column are 1-based.
        var edit = new TextEditDto(1, 0, 1, 1, "oops");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None));
        StringAssert.Contains(ex.Message, "1-based");
    }

    [TestMethod]
    public async Task ApplyTextEdit_RejectsBadEdit_WithoutTouchingDisk()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        var edit = new TextEditDto(5, 10, 5, 5, "x"); // reversed
        try
        {
            await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None);
            Assert.Fail("Expected ArgumentException.");
        }
        catch (ArgumentException)
        {
            // expected
        }

        var afterReject = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, afterReject,
            "A rejected edit must not leave disk in a mutated state.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_OverlappingSpans_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Two edits on line 5 whose spans overlap (apply-text-edit-overlap).
        var e1 = new TextEditDto(5, 5, 5, 12, "x");
        var e2 = new TextEditDto(5, 6, 5, 20, "y");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { e1, e2 }, CancellationToken.None));
        StringAssert.Contains(ex.Message, "overlapping");
    }

    [TestMethod]
    public async Task ApplyTextEdit_CSharpSyntaxError_BlocksApply_ReturnsSyntaxErrors()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        // Remove the closing brace of the class (line 13: `}`).
        var edit = new TextEditDto(13, 1, 13, 2, "");
        var result = await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.SyntaxErrors);
        Assert.IsTrue(result.SyntaxErrors!.Count > 0, "Parser should report at least one error.");

        var after = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, after, "Syntax check must block disk write.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_SkipSyntaxCheck_AllowsInvalidCSharp()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var edit = new TextEditDto(13, 1, 13, 2, "");
        var result = await EditService.ApplyTextEditsAsync(
            workspace.WorkspaceId, dogFilePath, new[] { edit }, CancellationToken.None, skipSyntaxCheck: true);

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.SyntaxErrors);
    }

    // ------------------------------------------------------------------
    // revert-last-apply-disk-consistency
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Revert_AfterDiskDriftWithEmptySolutionDiff_RestoresDisk()
    {
        // Reproduces the NetworkDocumentation audit bug: a snapshot was captured, the file was
        // mutated directly on disk (simulating the FLAG-9A path where MSBuildWorkspace doesn't
        // reflect the disk change), and revert must still restore the original content via the
        // explicit file-snapshot path (UndoService now captures these on apply_text_edit).
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        // Normal apply: capture the file snapshot on the undo stack.
        var lines = originalText.Split('\n');
        var appendEdit = new TextEditDto(lines.Length, lines[^1].Length + 1, lines.Length, lines[^1].Length + 1, "\n// apply marker");
        var applyResult = await EditService.ApplyTextEditsAsync(workspaceId, dogFilePath, new[] { appendEdit }, CancellationToken.None);
        Assert.IsTrue(applyResult.Success);

        // Simulate post-apply disk drift: a second out-of-band write that the workspace
        // Solution cannot see (the classic "disk ahead of workspace" FLAG-9A state).
        await File.AppendAllTextAsync(dogFilePath, "\n// drift from out-of-band edit", CancellationToken.None);

        // Revert must restore the original byte-for-byte — the file-snapshot fast path
        // writes the pre-apply text regardless of any out-of-band drift.
        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted, "Revert must report success.");

        var afterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, afterRevert,
            "Disk must match the pre-apply text after revert, even when drift happened between apply and revert.");
    }

    // ------------------------------------------------------------------
    // set-editorconfig-option-not-undoable
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task SetEditorConfigOption_ThenRevert_RestoresExistingContent()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Seed a pre-existing .editorconfig so we can verify restore-to-original (not delete).
        var editorconfigPath = Path.Combine(workspace.GetPath("SampleLib"), ".editorconfig");
        var originalContent = "root = true\n\n[*.{cs,csx,cake}]\nindent_size = 4\n";
        await File.WriteAllTextAsync(editorconfigPath, originalContent, CancellationToken.None);

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, "dotnet_diagnostic.CA1000.severity", "warning", CancellationToken.None);
        Assert.AreEqual(editorconfigPath, setResult.EditorConfigPath);
        Assert.IsFalse(setResult.CreatedNewFile);

        var afterSet = await File.ReadAllTextAsync(editorconfigPath, CancellationToken.None);
        StringAssert.Contains(afterSet, "dotnet_diagnostic.CA1000.severity = warning");

        var undoEntry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(undoEntry, "set_editorconfig_option must register an undo entry.");
        StringAssert.Contains(undoEntry.Description, ".editorconfig");

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted);

        var afterRevert = await File.ReadAllTextAsync(editorconfigPath, CancellationToken.None);
        Assert.AreEqual(originalContent, afterRevert,
            "Revert must restore the .editorconfig to its pre-write content byte-for-byte.");
    }

    [TestMethod]
    public async Task SetEditorConfigOption_CreatesNewFile_RevertDeletesIt()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Ensure no .editorconfig exists under the SampleLib folder before the call.
        var candidatePath = Path.Combine(workspace.GetPath("SampleLib"), ".editorconfig");
        if (File.Exists(candidatePath)) File.Delete(candidatePath);

        var setResult = await EditorConfigService.SetOptionAsync(
            workspaceId, dogFilePath, "dotnet_diagnostic.CA1001.severity", "warning", CancellationToken.None);
        // The implementation may locate an existing .editorconfig higher in the tree; only assert
        // the created-on-this-call case, which is what the "revert deletes it" contract covers.
        if (!setResult.CreatedNewFile)
        {
            Assert.Inconclusive("Test environment already had an .editorconfig higher in the directory tree.");
            return;
        }

        Assert.IsTrue(File.Exists(setResult.EditorConfigPath), "Set should have created the .editorconfig file.");

        var reverted = await UndoService.RevertAsync(workspaceId, CancellationToken.None);
        Assert.IsTrue(reverted);

        Assert.IsFalse(File.Exists(setResult.EditorConfigPath),
            "Revert of a newly-created .editorconfig must delete the file (pre-apply OriginalText was null).");
    }
}
