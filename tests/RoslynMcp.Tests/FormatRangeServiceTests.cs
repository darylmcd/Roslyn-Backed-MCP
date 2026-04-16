namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for format-range-preview-nonfunctional. Pre-fix, every parameter combination
/// produced an uninterpreted ArgumentOutOfRangeException; post-fix the service returns a
/// proper RefactoringPreviewDto for valid inputs and a structured ArgumentException for
/// invalid ranges.
/// </summary>
[TestClass]
public sealed class FormatRangeServiceTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task FormatRangePreview_ValidRange_ReturnsPreview()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // Format lines 11-17 of RefactoringProbe.cs (the ComputeAndPrint method body).
        // The file is already formatted; expect either an empty diff or a no-op preview.
        var preview = await RefactoringService.PreviewFormatRangeAsync(
            workspace.WorkspaceId,
            filePath,
            startLine: 11, startColumn: 1,
            endLine: 17, endColumn: 6,
            CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
        StringAssert.Contains(preview.Description, "Format range");
        StringAssert.Contains(preview.Description, "11-17");
    }

    [TestMethod]
    public async Task FormatRangePreview_InvertedRange_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            RefactoringService.PreviewFormatRangeAsync(
                workspace.WorkspaceId, filePath,
                startLine: 17, startColumn: 1,
                endLine: 11, endColumn: 6,
                CancellationToken.None));
        StringAssert.Contains(ex.Message, ">=");
    }

    [TestMethod]
    public async Task FormatRangePreview_ZeroColumn_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            RefactoringService.PreviewFormatRangeAsync(
                workspace.WorkspaceId, filePath,
                startLine: 11, startColumn: 0,
                endLine: 17, endColumn: 6,
                CancellationToken.None));
        StringAssert.Contains(ex.Message, "startColumn");
    }

    [TestMethod]
    public async Task FormatRangePreview_StartLinePastEof_Throws()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            RefactoringService.PreviewFormatRangeAsync(
                workspace.WorkspaceId, filePath,
                startLine: 99999, startColumn: 1,
                endLine: 99999, endColumn: 1,
                CancellationToken.None));
        StringAssert.Contains(ex.Message, "past the end");
    }

    [TestMethod]
    public async Task FormatRangePreview_SingleLineRange_ReturnsPreview()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        var preview = await RefactoringService.PreviewFormatRangeAsync(
            workspace.WorkspaceId, filePath,
            startLine: 13, startColumn: 1,
            endLine: 13, endColumn: 30,
            CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
    }

    [TestMethod]
    public async Task FormatRangePreview_ColumnPastLineEnd_ClampsRatherThanThrowing()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var filePath = workspace.GetPath("SampleLib", "RefactoringProbe.cs");

        // Pass a column wildly past the line end. The service clamps to the line/file end
        // so callers don't need to count line widths to format a range.
        var preview = await RefactoringService.PreviewFormatRangeAsync(
            workspace.WorkspaceId, filePath,
            startLine: 11, startColumn: 1,
            endLine: 17, endColumn: 9999,
            CancellationToken.None);

        Assert.IsNotNull(preview.PreviewToken);
    }

    /// <summary>
    /// Regression for `format-range-preview-empty-diff-compile-check-filter-false-clean`
    /// (P3) and `dr-9-12-flag-format-range-empty-returns-empty-diff-on-d` (P4):
    /// formatting a dirty range used to return a `unifiedDiff` with no `@@` hunks
    /// while the apply path still mutated the file. Whole-document format + intersect
    /// against the requested span makes the preview match what apply produces.
    /// </summary>
    [TestMethod]
    public async Task FormatRangePreview_DirtyRange_EmitsNonEmptyDiff_ApplyMatches()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Deliberately bad formatting: extra spaces around tokens that the Roslyn formatter
        // will collapse. The dirty region is lines 7-9 ("var doubled = input *  2 ;" with
        // double spaces and space-before-semicolon — these are not line-ending differences,
        // so DiffPlex will see them as line content changes).
        var fixturePath = Path.Combine(sampleLibDir, "FormatRangeRegressionFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class FormatRangeRegressionFixture",
            "{",
            "    public int Compute(int input)",
            "    {",
            "        var doubled = input *  2 ;",
            "        var tripled = doubled  + input ;",
            "        return  tripled ;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Range covers only the mis-indented body lines (7-9). Pre-fix this
            // produced an empty `unifiedDiff` (no `@@` hunk) while apply still rewrote
            // the file via the stored solution. Post-fix the preview enumerates the
            // formatter's edits and apply produces the same on-disk text.
            var preview = await RefactoringService.PreviewFormatRangeAsync(
                workspaceId,
                fixturePath,
                startLine: 7, startColumn: 1,
                endLine: 9, endColumn: 30,
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            Assert.AreEqual(1, preview.Changes.Count, "preview must report one changed file");

            var change = preview.Changes[0];
            Assert.IsTrue(
                change.UnifiedDiff.Contains("@@", StringComparison.Ordinal),
                $"preview unifiedDiff must contain at least one hunk, got:\n{change.UnifiedDiff}");

            // Apply the preview and compare on-disk text to the preview's expected text.
            var preApplyText = await File.ReadAllTextAsync(fixturePath);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "apply must succeed");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            Assert.AreNotEqual(preApplyText, postApplyText,
                "apply must mutate the file when preview reports changes (non-empty diff)");

            // The formatter collapsed double-spaces and removed space-before-semicolon
            // on the targeted lines.
            StringAssert.Contains(postApplyText, "var doubled = input * 2;");
            StringAssert.Contains(postApplyText, "var tripled = doubled + input;");
            StringAssert.Contains(postApplyText, "return tripled;");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }

    /// <summary>
    /// Companion to <see cref="FormatRangePreview_DirtyRange_EmitsNonEmptyDiff_ApplyMatches"/>:
    /// when the dirty section sits entirely outside the requested range, the preview
    /// must report no changes (the bug fix must not over-correct by formatting unrelated
    /// regions of the file).
    /// </summary>
    [TestMethod]
    public async Task FormatRangePreview_DirtyOutsideRange_LeavesUnrelatedRegionAlone()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // CleanMethod is well-formatted. DirtyMethod has bad whitespace (double-space
        // and space-before-semicolon). Range covers only CleanMethod — DirtyMethod's
        // dirty tokens must NOT be touched by the apply. Explicit \r\n line endings
        // match the on-disk encoding the formatter produces on Windows so the diff
        // baseline isn't muddied by line-ending normalization.
        var fixturePath = Path.Combine(sampleLibDir, "FormatRangeOutOfBoundsFixture.cs");
        var content = string.Join("\r\n", new[]
        {
            "namespace SampleLib;",
            "",
            "public class FormatRangeOutOfBoundsFixture",
            "{",
            "    public int CleanMethod(int input)",
            "    {",
            "        return input * 2;",
            "    }",
            "",
            "    public int DirtyMethod(int input)",
            "    {",
            "        return  input  + 1 ;",
            "    }",
            "}",
            "",
        });
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Range covers only `CleanMethod` (lines 5-8). DirtyMethod (lines 10-13)
            // must remain untouched even after apply.
            var preview = await RefactoringService.PreviewFormatRangeAsync(
                workspaceId,
                fixturePath,
                startLine: 5, startColumn: 1,
                endLine: 8, endColumn: 6,
                CancellationToken.None);

            Assert.IsNotNull(preview.PreviewToken);
            Assert.AreEqual(0, preview.Changes.Count,
                "no changes expected when the requested range is already clean");

            var preApplyText = await File.ReadAllTextAsync(fixturePath);
            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success);

            var postApplyText = await File.ReadAllTextAsync(fixturePath);
            Assert.AreEqual(preApplyText, postApplyText,
                "apply must be a no-op when preview reports no changes; DirtyMethod must remain dirty");
            StringAssert.Contains(postApplyText, "return  input  + 1 ;");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }
}
