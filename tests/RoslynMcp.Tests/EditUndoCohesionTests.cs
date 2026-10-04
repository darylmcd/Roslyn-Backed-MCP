using System.Reflection;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Tests.Helpers;

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
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    public async Task InvalidEditRefusals_PublishSafeGuidanceWithoutMutatingState(int caseIndex)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var path = workspace.GetPath("SampleLib", "Dog.cs");
        var beforeBytes = await File.ReadAllBytesAsync(path);
        var beforeSolution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var beforeVersion = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var document = beforeSolution.Projects.SelectMany(project => project.Documents).Single(document => document.FilePath == path);
        var source = await document.GetTextAsync();
        var firstLineLength = source.Lines[0].SpanIncludingLineBreak.Length;
        var beforeUndo = UndoService.GetLastOperation(workspace.WorkspaceId);
        const string sentinel = "submitted-text-must-stay-private";
        var cases = new (TextEditDto[] Edits, string Guidance)[]
        {
            ([], "At least one text edit"),
            ([new(1, 1, 1, 1, null!)], "Edit #0 has a null NewText"),
            ([new(0, 1, 1, 1, sentinel)], "(0,1)-(1,1)"),
            ([new(9999, 1, 9999, 1, sentinel)], $"references line 9999 but the file only has {source.Lines.Count} line(s)"),
            ([new(1, 500, 1, 500, sentinel)], $"StartColumn 500 but line 1 only has {firstLineLength} character(s)"),
            ([new(1, 1, 1, 500, sentinel)], $"EndColumn 500 but line 1 only has {firstLineLength} character(s)"),
            ([new(5, 10, 5, 5, sentinel)], "start (5,10) is after end (5,5)"),
            ([new(5, 5, 5, 12, sentinel), new(5, 6, 5, 20, sentinel)], "Edits #0 and #1 have overlapping spans"),
        };
        var (edits, guidance) = cases[caseIndex];
        foreach (var mode in new[] { "apply", "batch", "preview" })
        {
            foreach (var wrapped in new[] { false, true })
            {
                var tool = mode switch
                {
                    "preview" => "preview_multi_file_edit",
                    "batch" => "apply_multi_file_edit",
                    _ => "apply_text_edit",
                };
                var payload = await ToolExecutionTestHarness.RunAsync(tool, async () =>
                {
                    try
                    {
                        if (mode == "apply")
                            await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, path, edits, tool, CancellationToken.None);
                        else if (mode == "batch")
                            await EditService.ApplyMultiFileTextEditsAsync(workspace.WorkspaceId, [new(path, edits)], tool, CancellationToken.None);
                        else
                            await EditService.PreviewMultiFileTextEditsAsync(workspace.WorkspaceId, [new(path, edits)], CancellationToken.None);
                        Assert.Fail("Invalid edits must be refused.");
                        return "{}";
                    }
                    catch (ArgumentException ex)
                    {
                        Assert.AreEqual("edits", ex.ParamName);
                        if (wrapped) throw new TargetInvocationException(ex);
                        throw;
                    }
                });
                using var json = JsonDocument.Parse(payload);
                var envelope = json.RootElement;
                Assert.AreEqual("InvalidArgument", envelope.GetProperty("category").GetString(), payload);
                Assert.AreEqual(wrapped ? "TargetInvocationException" : "ArgumentException", envelope.GetProperty("exceptionType").GetString(), payload);
                StringAssert.Contains(envelope.GetProperty("schemaHint").GetString()!, mode == "apply" ? "edits" : "fileEdits", StringComparison.Ordinal);
                StringAssert.Contains(envelope.GetProperty("message").GetString()!, guidance, StringComparison.Ordinal);
                Assert.IsFalse(payload.Contains(path, StringComparison.Ordinal));
                Assert.IsFalse(payload.Contains(Path.GetFileName(path), StringComparison.Ordinal));
                Assert.IsFalse(payload.Contains(sentinel, StringComparison.Ordinal));
                CollectionAssert.AreEqual(beforeBytes, await File.ReadAllBytesAsync(path));
                Assert.AreSame(beforeSolution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
                Assert.AreEqual(beforeVersion, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
                Assert.AreSame(beforeUndo, UndoService.GetLastOperation(workspace.WorkspaceId));
            }
        }

        var insertion = new TextEditDto(1, 1, 1, 1, "// valid insert\n");
        var preview = await EditService.PreviewMultiFileTextEditsAsync(workspace.WorkspaceId, [new(path, new[] { insertion })], CancellationToken.None);
        Assert.IsNotNull(preview);
        CollectionAssert.AreEqual(beforeBytes, await File.ReadAllBytesAsync(path));
        Assert.AreSame(beforeSolution, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreSame(beforeUndo, UndoService.GetLastOperation(workspace.WorkspaceId));
        var applied = await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, path, [insertion], "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(applied.Success);
        StringAssert.StartsWith(await File.ReadAllTextAsync(path), "// valid insert\n");
        Assert.IsNotNull(UndoService.GetLastOperation(workspace.WorkspaceId));
        Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId, CancellationToken.None));
        CollectionAssert.AreEqual(beforeBytes, await File.ReadAllBytesAsync(path));
        var batch = await EditService.ApplyMultiFileTextEditsAsync(workspace.WorkspaceId, [new(path, new[] { insertion })], "apply_multi_file_edit", CancellationToken.None);
        Assert.IsTrue(batch.Success);
        StringAssert.StartsWith(await File.ReadAllTextAsync(path), "// valid insert\n");
        Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId, CancellationToken.None));
        CollectionAssert.AreEqual(beforeBytes, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task ApplyTextEdit_NullNewText_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Intentionally build a malformed edit with null NewText. TextEditDto is a positional
        // record; null! supplies malformed input while bypassing nullable analysis.
        var edit = new TextEditDto(1, 1, 1, 1, null!);

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "null NewText");
    }

    [TestMethod]
    public async Task ApplyTextEdit_ReversedRange_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Start (5,10) → End (5,5): end precedes start on the same line.
        var edit = new TextEditDto(5, 10, 5, 5, "x");

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "reversed range");
    }

    [TestMethod]
    public async Task ApplyTextEdit_OutOfBoundsLine_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Line 9999 is far beyond the file length.
        var edit = new TextEditDto(9999, 1, 9999, 1, "oops");

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "9999");
    }

    [TestMethod]
    public async Task ApplyTextEdit_NonPositiveColumn_ThrowsArgumentException()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Column 0 is invalid — line/column are 1-based.
        var edit = new TextEditDto(1, 0, 1, 1, "oops");

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "1-based");
    }

    [TestMethod]
    public async Task ApplyTextEdit_StartColumnPastEndOfLine_ThrowsArgumentException()
    {
        // edit-preview-validation-decomposition: the StartColumn-overflow branch of the extracted
        // ValidateEditBounds. Line 1 of Dog.cs is `namespace SampleLib;` — column 500 is far past its
        // one-past-the-end limit, and the failure must name StartColumn (not EndColumn).
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var edit = new TextEditDto(1, 500, 1, 500, "oops");

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "StartColumn 500", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ApplyTextEdit_EndColumnPastEndOfLine_ThrowsArgumentException()
    {
        // edit-preview-validation-decomposition: the EndColumn-overflow branch of the extracted
        // ValidateEditBounds. StartColumn is valid here, so this can only trip the *end* check —
        // pinning the branch order (start-overflow before end-overflow before reversed-range).
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var edit = new TextEditDto(1, 1, 1, 500, "oops");

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None));
        StringAssert.Contains(ex.Message, "EndColumn 500", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ApplyTextEdit_WholeLineReplacement_PreservesLfTerminator()
    {
        // edit-preview-validation-decomposition: exercises the extracted
        // AdjustReplacementForTrailingLineBreak. A span ending at column 1 of the NEXT line swallows
        // line 5's terminator; the replacement carries none of its own, so the LF must be
        // re-appended or lines 5 and 6 silently merge.
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        var edit = new TextEditDto(5, 1, 6, 1, "    public string Name => \"Cat\";");
        var result = await EditService.ApplyTextEditsAsync(
            workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success);

        var after = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText.Replace("\"Dog\"", "\"Cat\"", StringComparison.Ordinal), after,
            "A whole-line replacement whose span ends at column 1 must preserve the swallowed line terminator.");
    }

    [TestMethod]
    public async Task ApplyTextEdit_WholeLineReplacementInCrlfFile_PreservesCrlfTerminator()
    {
        // edit-preview-validation-decomposition, risk (b): the \r\n branch of the extracted
        // AdjustReplacementForTrailingLineBreak. Detection probes for the paired \r BEFORE settling
        // for a bare \n — without that ordering a CRLF file silently degrades to mixed endings on
        // every whole-line replacement, with no compile-time or syntax-check signal.
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var crlfPath = workspace.GetPath("SampleLib", "CrlfLineEndingFixture.cs");
        const string crlfSource =
            "namespace SampleLib;\r\n" +
            "\r\n" +
            "public class CrlfLineEndingFixture\r\n" +
            "{\r\n" +
            "    public string Value => \"before\";\r\n" +
            "}\r\n";
        await File.WriteAllTextAsync(crlfPath, crlfSource, CancellationToken.None);
        await workspace.LoadAsync(CancellationToken.None);

        var edit = new TextEditDto(5, 1, 6, 1, "    public string Value => \"after\";");
        var result = await EditService.ApplyTextEditsAsync(
            workspace.WorkspaceId, crlfPath, new[] { edit }, "apply_text_edit", CancellationToken.None);
        Assert.IsTrue(result.Success);

        var after = await File.ReadAllTextAsync(crlfPath, CancellationToken.None);
        Assert.AreEqual(crlfSource.Replace("\"before\"", "\"after\"", StringComparison.Ordinal), after,
            "A whole-line replacement in a CRLF file must re-append \\r\\n, not a bare \\n.");
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
            await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);
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

        var ex = await Assert.ThrowsExactlyAsync<PublicArgumentException>(() =>
            EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { e1, e2 }, "apply_text_edit", CancellationToken.None));
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
        var result = await EditService.ApplyTextEditsAsync(workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None);

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
            workspace.WorkspaceId, dogFilePath, new[] { edit }, "apply_text_edit", CancellationToken.None, skipSyntaxCheck: true);

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
        var applyResult = await EditService.ApplyTextEditsAsync(workspaceId, dogFilePath, new[] { appendEdit }, "apply_text_edit", CancellationToken.None);
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
            workspaceId, dogFilePath, "dotnet_diagnostic.CA1000.severity", "warning", "set_editorconfig_option", CancellationToken.None);
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
    public async Task SetEditorConfigOption_RevertWaitsForTransientReadHandle()
    {
        if (!OperatingSystem.IsWindows()) return;

        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var source = workspace.GetPath("SampleLib", "Dog.cs");
        var configPath = workspace.GetPath("SampleLib", ".editorconfig");
        const string original = "[*.cs]\nindent_size = 4\n";
        await File.WriteAllTextAsync(configPath, original);
        await EditorConfigService.SetOptionAsync(workspace.WorkspaceId, source,
            "indent_size", "8", "set_editorconfig_option", CancellationToken.None);

        Task<bool> revert;
        using (var reader = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            revert = UndoService.RevertAsync(workspace.WorkspaceId);
            var first = await Task.WhenAny(revert, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.AreNotSame(revert, first,
                "A temporary reader must leave the undo pending until the reader releases its handle.");
        }

        Assert.IsTrue(await revert);
        Assert.AreEqual(original, await File.ReadAllTextAsync(configPath));
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
            workspaceId, dogFilePath, "dotnet_diagnostic.CA1001.severity", "warning", "set_editorconfig_option", CancellationToken.None);
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
