using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>semantic-edit-with-compile-check-wrapper</c>:
/// <c>apply_text_edit</c> and <c>apply_multi_file_edit</c> accept <c>verify</c> +
/// <c>autoRevertOnError</c> parameters. The verify pass runs <c>compile_check</c>
/// scoped to the owning project after the edit and filters pre-existing errors out
/// via a pre-vs-post fingerprint diff, so pre-existing errors are never attributed
/// to the current call. <c>autoRevertOnError=true</c> triggers a single-shot revert
/// through the same snapshot slot the call captured.
/// </summary>
[TestClass]
public sealed class ApplyTextEditVerifyTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    // ------------------------------------------------------------------
    // Default-path preservation: verify omitted → no Verification field.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_VerifyFalse_OmitsVerificationField()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var edit = AppendCommentEdit(originalText, "// verify-false probe");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId,
            dogFilePath,
            new[] { edit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: false,
            autoRevertOnError: false);

        Assert.IsTrue(result.Success, "Edit with verify=false must apply cleanly.");
        Assert.IsNull(result.Verification, "Default path must not attach a Verification field.");

        var afterEdit = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterEdit, "// verify-false probe");
    }

    // ------------------------------------------------------------------
    // verify=true + clean edit → Verification.Status == "clean".
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_VerifyTrue_CleanEdit_ReturnsStatusClean()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var edit = AppendCommentEdit(originalText, "// clean-verify probe");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId,
            dogFilePath,
            new[] { edit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: true,
            autoRevertOnError: false);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Verification, "verify=true must populate the Verification field.");
        Assert.AreEqual("clean", result.Verification!.Status);
        Assert.AreEqual(0, result.Verification.NewDiagnostics.Count);
        Assert.AreEqual("SampleLib", result.Verification.ProjectFilter,
            "Single-file edit should scope verify to the owning project.");
    }

    // ------------------------------------------------------------------
    // verify=true + compile error + autoRevertOnError=false →
    // Status == "errors_introduced"; file on disk still has the bad edit.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_VerifyTrue_CompileError_NoAutoRevert_PreservesBrokenState()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);

        // Break Speak's return expression with an undefined symbol. Syntax-valid
        // (so the pre-apply syntax check passes), but compile-invalid (so the verify
        // pass MUST flag it as a new error).
        var edit = ReplaceSpeakReturnEdit(originalText, "UndefinedSymbolForCompileError");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId,
            dogFilePath,
            new[] { edit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: true,
            autoRevertOnError: false);

        Assert.IsTrue(result.Success, "Core apply should succeed — the edit is syntactically valid.");
        Assert.IsNotNull(result.Verification);
        Assert.AreEqual("errors_introduced", result.Verification!.Status);
        Assert.IsTrue(result.Verification.NewDiagnostics.Count > 0,
            "Verification must list at least one new compile diagnostic.");

        // File on disk should still have the broken edit (no auto-revert).
        var afterEdit = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterEdit, "UndefinedSymbolForCompileError");

        // The undo snapshot still carries the pre-edit state so the caller can
        // manually call revert_last_apply.
        var undoEntry = UndoService.GetLastOperation(workspaceId);
        Assert.IsNotNull(undoEntry, "Undo snapshot must remain so the caller can revert manually.");
    }

    // ------------------------------------------------------------------
    // verify=true + compile error + autoRevertOnError=true →
    // Status == "reverted"; file on disk matches original byte-for-byte.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_VerifyTrue_CompileError_AutoRevert_RollsBackEdit()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        var originalText = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var edit = ReplaceSpeakReturnEdit(originalText, "UndefinedSymbolForRevertPath");

        var result = await EditService.ApplyTextEditsAsync(
            workspaceId,
            dogFilePath,
            new[] { edit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: true,
            autoRevertOnError: true);

        Assert.IsTrue(result.Success, "Core apply should still report Success=true — the rollback is recorded in Verification.");
        Assert.IsNotNull(result.Verification);
        Assert.AreEqual("reverted", result.Verification!.Status);
        Assert.IsTrue(result.Verification.NewDiagnostics.Count > 0);

        // File on disk must match the original byte-for-byte — the auto-revert
        // used the same undo slot this call captured.
        var afterRevert = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        Assert.AreEqual(originalText, afterRevert,
            "After auto-revert, Dog.cs must match the original byte-for-byte.");
        Assert.IsFalse(afterRevert.Contains("UndefinedSymbolForRevertPath", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // Pre-vs-post filter: a compile error left in place by a PRIOR call
    // (autoRevertOnError=false) must NOT cause a later benign call's
    // verify to report "errors_introduced" — the filter treats that
    // error as pre-existing.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyTextEdit_VerifyTrue_DoesNotFlagPreExistingErrors()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        // Call 1: break Cat.cs with an undefined symbol. verify=false so the broken
        // state is left in place; no auto-revert.
        var originalCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        var catEdit = ReplaceSpeakReturnEdit(originalCat, "PreExistingErrorMarker");
        var call1 = await EditService.ApplyTextEditsAsync(
            workspaceId, catFilePath, new[] { catEdit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false, verify: false, autoRevertOnError: false);
        Assert.IsTrue(call1.Success);
        Assert.IsNull(call1.Verification, "Sanity: call 1 should not have a Verification field.");

        // Sanity: workspace now has at least one compile error.
        var baselineCheck = await CompileCheckService.CheckAsync(
            workspaceId,
            new CompileCheckOptions(ProjectFilter: "SampleLib", SeverityFilter: "error", Limit: 50),
            CancellationToken.None);
        Assert.IsTrue(baselineCheck.ErrorCount > 0,
            "Pre-existing error baseline must be non-zero for this test to be meaningful.");

        // Call 2: benign edit to Dog.cs with verify=true AND autoRevertOnError=true.
        // The pre-existing error from call 1 must NOT be attributed to call 2, so
        // the verify outcome should be "clean" and the file should keep the edit.
        var originalDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var dogEdit = AppendCommentEdit(originalDog, "// benign-in-broken-workspace");
        var call2 = await EditService.ApplyTextEditsAsync(
            workspaceId, dogFilePath, new[] { dogEdit }, "apply_text_edit", CancellationToken.None,
            skipSyntaxCheck: false, verify: true, autoRevertOnError: true);

        Assert.IsTrue(call2.Success);
        Assert.IsNotNull(call2.Verification);
        Assert.AreEqual("clean", call2.Verification!.Status,
            "Pre-existing errors from call 1 must be filtered out of call 2's verify outcome.");
        Assert.IsTrue(call2.Verification.PreErrorCount > 0,
            "PreErrorCount must reflect the pre-existing errors so the caller can inspect them.");

        // Dog edit survives — the benign edit was not auto-reverted.
        var afterCall2 = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        StringAssert.Contains(afterCall2, "// benign-in-broken-workspace");
    }

    // ------------------------------------------------------------------
    // apply_multi_file_edit — symmetry with apply_text_edit.
    // verify=true + clean edits → Verification.Status == "clean".
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyMultiFileEdit_VerifyTrue_CleanEdits_ReturnsStatusClean()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var originalDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var originalCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        var fileEdits = new[]
        {
            new FileEditsDto(dogFilePath, new[] { AppendCommentEdit(originalDog, "// multi-file verify dog") }),
            new FileEditsDto(catFilePath, new[] { AppendCommentEdit(originalCat, "// multi-file verify cat") }),
        };

        var result = await EditService.ApplyMultiFileTextEditsAsync(
            workspaceId,
            fileEdits, "apply_multi_file_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: true,
            autoRevertOnError: false);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.FilesModified);
        Assert.IsNotNull(result.Verification, "apply_multi_file_edit verify=true must populate Verification.");
        Assert.AreEqual("clean", result.Verification!.Status);
    }

    // ------------------------------------------------------------------
    // apply_multi_file_edit — auto-revert on a batch that introduces a
    // compile error. All files in the batch must be restored to pre-edit
    // state because the undo snapshot is batch-level (single-slot).
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ApplyMultiFileEdit_VerifyTrue_CompileError_AutoRevert_RollsBackWholeBatch()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");

        var originalDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var originalCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);

        var fileEdits = new[]
        {
            // Benign edit in Dog.cs that would survive on its own.
            new FileEditsDto(dogFilePath, new[] { AppendCommentEdit(originalDog, "// batch-revert dog") }),
            // Cat.cs edit introduces an undefined symbol, failing the batch's compile.
            new FileEditsDto(catFilePath, new[] { ReplaceSpeakReturnEdit(originalCat, "BatchRevertMarker") }),
        };

        var result = await EditService.ApplyMultiFileTextEditsAsync(
            workspaceId,
            fileEdits, "apply_multi_file_edit", CancellationToken.None,
            skipSyntaxCheck: false,
            verify: true,
            autoRevertOnError: true);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Verification);
        Assert.AreEqual("reverted", result.Verification!.Status);
        Assert.IsTrue(result.Verification.NewDiagnostics.Count > 0);

        // Both files must match the original — the batch-level snapshot rolled back
        // the benign edit alongside the breaking one.
        var afterDog = await File.ReadAllTextAsync(dogFilePath, CancellationToken.None);
        var afterCat = await File.ReadAllTextAsync(catFilePath, CancellationToken.None);
        Assert.AreEqual(originalDog, afterDog,
            "Dog.cs must match the original after batch-level auto-revert.");
        Assert.AreEqual(originalCat, afterCat,
            "Cat.cs must match the original after batch-level auto-revert.");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static TextEditDto AppendCommentEdit(string fileText, string commentLine)
    {
        var lines = fileText.Split('\n');
        var lastLine = lines.Length;
        var lastColumn = lines[^1].Length + 1;
        return new TextEditDto(lastLine, lastColumn, lastLine, lastColumn, "\n" + commentLine);
    }

    /// <summary>
    /// Replaces the <c>"Woof"</c> / <c>"Meow"</c> string literal in the
    /// <c>Speak() =&gt; ...</c> expression body with an undefined identifier. Produces a
    /// compile error (CS0103) but keeps the syntax tree valid, so the pre-apply syntax
    /// check passes and the verify pass is the only layer that can catch the breakage.
    /// </summary>
    private static TextEditDto ReplaceSpeakReturnEdit(string fileText, string replacementIdentifier)
    {
        var lines = fileText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("Speak()", StringComparison.Ordinal) ||
                !lines[i].Contains("=>", StringComparison.Ordinal))
            {
                continue;
            }

            // Replace the contents AFTER "=>" up to the trailing semicolon.
            // Preserve the "    public string Speak() => " prefix so the syntax
            // tree remains well-formed.
            var arrowIndex = lines[i].IndexOf("=>", StringComparison.Ordinal);
            var semicolonIndex = lines[i].LastIndexOf(';');
            if (arrowIndex < 0 || semicolonIndex <= arrowIndex)
            {
                throw new InvalidOperationException(
                    "Expected 'public string Speak() => \"...\";' pattern — test fixture drift?");
            }

            // TextEditDto is 1-based (line, column). `arrowIndex + 2` + 1 gives the
            // column right after "=> "; use the space-skipped start column.
            var startColumn = arrowIndex + 3 + 1; // +2 for "=>", +1 for space, +1 for 1-based.
            var endColumn = semicolonIndex + 1;   // exclusive-1-based end at the semicolon.
            return new TextEditDto(i + 1, startColumn, i + 1, endColumn, replacementIdentifier);
        }

        throw new InvalidOperationException("Could not locate Speak() expression body in test fixture.");
    }
}
