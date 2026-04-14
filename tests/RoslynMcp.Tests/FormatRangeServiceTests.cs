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
}
