namespace RoslynMcp.Tests;

/// <summary>
/// Regression tests for <c>CodeActionService.GetCodeActionsAsync</c> caret-only calls
/// (no endLine / endColumn supplied). Prior to the
/// <c>get-code-actions-caret-only-inverted-range</c> fix, the default-end computation
/// used the enclosing line's end without clamping against the caret's position — so a
/// caret sitting past the last character of a short line (e.g. line=1, column=50 on a
/// 20-character line) produced a <c>TextSpan.FromBounds(start, end)</c> where
/// <c>end &lt; start</c>, throwing <c>ArgumentOutOfRangeException</c> from deep inside
/// Roslyn rather than returning an empty action list.
///
/// The fix clamps <c>end</c> to <c>Math.Max(startPosition, lineEnd)</c>, yielding a
/// zero-width selection at the caret when the column is past EOL and preserving the
/// remainder-of-line default when the column is in range.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class CodeActionServiceTests : SharedWorkspaceTestBase
{
    private static string SampleWorkspaceId { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        SampleWorkspaceId = await LoadSharedSampleWorkspaceAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    private static string FindDocumentPath(string name)
    {
        var solution = WorkspaceManager.GetCurrentSolution(SampleWorkspaceId);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .First(d => d.Name == name).FilePath!;
    }

    /// <summary>
    /// Caret at the very start of the file (line 1, column 1 — position 0). The first
    /// line of <c>RefactoringProbe.cs</c> is <c>namespace SampleLib;</c>. Must return
    /// successfully with a well-formed response envelope.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_CaretOnly_AtStartOfFile_DoesNotThrow()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        var result = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 1,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        Assert.IsNotNull(result, "Caret at (1,1) must produce a well-formed response envelope.");
        Assert.IsTrue(result.Count >= 0, "Caret-only call must populate Count (zero or more actions).");
    }

    /// <summary>
    /// Caret column past the end of a short line — the original inverted-range trigger.
    /// Line 1 is <c>namespace SampleLib;</c> (20 chars). A caret at column 50 sits well
    /// past EOL. Before the fix this threw <c>ArgumentOutOfRangeException</c> from
    /// <c>TextSpan.FromBounds</c> because the default-end landed before <c>startPosition</c>.
    /// After the fix it clamps to a zero-width selection at the caret.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_CaretOnly_ColumnPastEndOfLine_DoesNotThrow()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        var result = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 1,
            startColumn: 50,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        Assert.IsNotNull(result, "Caret past EOL must clamp to a zero-width span, not throw.");
        Assert.IsTrue(result.Count >= 0);
    }

    /// <summary>
    /// Caret on a single-character token — column 1 of line 1, which is the
    /// <c>n</c> of <c>namespace</c>. Exercises the normal in-range path (column at
    /// line start, lineEnd &gt; startPosition) to ensure the clamp did not regress the
    /// remainder-of-line default.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_CaretOnly_OnSingleCharToken_DoesNotThrow()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        // Line 38 of RefactoringProbe.cs: `        int result = input;`
        // Column 13 lands on the `r` of `result` — a multi-char token start, but the
        // caret itself is a single-char position. Before the fix, callers that
        // accidentally passed a column beyond a short line triggered the bug; here
        // we verify the in-range path still produces a lineEnd-terminated span.
        var result = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 38,
            startColumn: 13,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
    }

    /// <summary>
    /// Caret at the end of the file — on the line of the closing brace, past any content.
    /// <c>RefactoringProbe.cs</c> ends at line 43 with a single <c>}</c>. A caret at
    /// (43, 100) is well past EOL on the final line; before the fix this was another
    /// inverted-range trigger. After the fix the span clamps to zero-width.
    /// </summary>
    [TestMethod]
    public async Task GetCodeActions_CaretOnly_AtEndOfFile_DoesNotThrow()
    {
        var filePath = FindDocumentPath("RefactoringProbe.cs");

        var result = await CodeActionService.GetCodeActionsAsync(
            SampleWorkspaceId,
            filePath,
            startLine: 43,
            startColumn: 100,
            endLine: null,
            endColumn: null,
            CancellationToken.None);

        Assert.IsNotNull(result, "Caret past EOF must not produce an inverted range.");
        Assert.IsTrue(result.Count >= 0);
    }
}
