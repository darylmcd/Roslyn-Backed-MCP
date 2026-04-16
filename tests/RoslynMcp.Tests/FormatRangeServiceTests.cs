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

    /// <summary>
    /// Regression for dr-9-7-only-partially-normalizes-whitespace: format_range_preview
    /// must clean up the full set of whitespace anomalies a caller would expect,
    /// not just inter-token spacing. The audit found three categories that survived a
    /// preview/apply round-trip pre-fix:
    /// <list type="number">
    ///   <item>Tab indentation where editorconfig says spaces (handled by
    ///   <c>Formatter.FormatAsync</c>'s indentation normalization).</item>
    ///   <item>Trailing whitespace at end-of-line (handled implicitly by the formatter
    ///   on lines where it rewrote the leading trivia, since the rewritten line carries
    ///   the formatter's trimmed trailing trivia).</item>
    ///   <item>Runs of two or more consecutive blank lines (NOT handled by the formatter
    ///   alone — needed an explicit collapse pass; that's the new fix).</item>
    /// </list>
    /// </summary>
    [TestMethod]
    public async Task FormatRangePreview_NormalizesAllWhitespaceAnomalies()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");

        // Anomaly catalogue (each must be cleaned up by format_range_preview):
        //   L7  : tab-indented inside a method body that .editorconfig wants spaces
        //   L8  : trailing whitespace after a statement
        //   L9..L11 : three consecutive blank lines (should collapse to one between
        //             statements / one before closing brace)
        //   L12 : another statement with trailing tab + spaces.
        //
        // Use \r\n explicitly so the diff baseline isn't affected by line-ending munging.
        var fixturePath = Path.Combine(sampleLibDir, "FormatRangeWhitespaceFixture.cs");
        var content =
            "namespace SampleLib;\r\n" +
            "\r\n" +
            "public class FormatRangeWhitespaceFixture\r\n" +
            "{\r\n" +
            "    public int Compute(int input)\r\n" +
            "    {\r\n" +
            "\tvar a = input + 1;\r\n" +                 // L7  tab indent
            "        var b = a + 2;   \r\n" +            // L8  trailing spaces
            "\r\n" +                                       // L9  blank
            "\r\n" +                                       // L10 blank
            "\r\n" +                                       // L11 blank
            "        var c = b + 3;\t  \r\n" +             // L12 trailing tab + spaces
            "        return c;\r\n" +
            "    }\r\n" +
            "}\r\n";
        await File.WriteAllTextAsync(fixturePath, content);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        var workspaceId = loadResult.WorkspaceId;

        try
        {
            // Range covers the full method body (L6..L14) so the formatter has every
            // anomaly inside the requested span.
            var preview = await RefactoringService.PreviewFormatRangeAsync(
                workspaceId, fixturePath,
                startLine: 6, startColumn: 1,
                endLine: 14, endColumn: 6,
                CancellationToken.None);

            var applyResult = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken!, CancellationToken.None);
            Assert.IsTrue(applyResult.Success, "apply must succeed");

            var postApplyText = await File.ReadAllTextAsync(fixturePath);

            // (1) tab indent normalized to 4 spaces
            Assert.IsFalse(
                postApplyText.Contains("\tvar a", StringComparison.Ordinal),
                $"tab-indented line should be re-indented with spaces. Got:\n{postApplyText}");

            // (2) trailing whitespace stripped
            foreach (var line in postApplyText.Split("\r\n"))
            {
                var endsWithWhitespace = line.Length > 0 && (line[^1] == ' ' || line[^1] == '\t');
                Assert.IsFalse(endsWithWhitespace,
                    $"trailing whitespace found on line: '{line.Replace("\t", "<TAB>").Replace(" ", "<SP>")}'\nFull text:\n{postApplyText}");
            }

            // (3) Multiple consecutive blank lines collapsed to at most one inside the body.
            Assert.IsFalse(
                postApplyText.Contains("\r\n\r\n\r\n", StringComparison.Ordinal),
                $"more than one consecutive blank line survived inside the body. Got:\n{postApplyText}");
        }
        finally
        {
            WorkspaceManager.Close(workspaceId);
        }
    }
}
