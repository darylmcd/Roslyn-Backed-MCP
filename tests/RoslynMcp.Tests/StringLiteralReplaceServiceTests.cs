using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <see cref="StringLiteralReplaceService.PreviewReplaceAsync"/>.
/// Backlog row replace-string-literals-preview-throws-on-zero-match: the zero-match path
/// previously threw <see cref="InvalidOperationException"/>; now it must return a
/// structured empty preview (empty token, empty changes list, descriptive Description)
/// matching the shape used by FixAllService.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class StringLiteralReplaceServiceTests : SharedWorkspaceTestBase
{
    private static string WorkspaceId { get; set; } = null!;
    private static StringLiteralReplaceService Service { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        InitializeServices();
        WorkspaceId = await GetOrLoadWorkspaceIdAsync(SampleSolutionPath, CancellationToken.None);
        Service = new StringLiteralReplaceService(WorkspaceManager, PreviewStore);
    }

    [TestMethod]
    public async Task PreviewReplace_ZeroMatches_ReturnsEmptyPreviewInsteadOfThrowing()
    {
        // A literal that cannot appear anywhere in the sample workspace. Pre-fix this threw
        // `InvalidOperationException("... no matching literals found in scope.")`; now the
        // service must return a structured empty response so callers can treat "no matches"
        // as a benign outcome rather than an error.
        var replacements = new[]
        {
            new StringLiteralReplacementDto(
                LiteralValue: "UNLIKELY_LITERAL_THAT_DOES_NOT_EXIST_XYZ_8f2a1c",
                ReplacementExpression: "Constants.Unused",
                UsingNamespace: null),
        };

        var preview = await Service.PreviewReplaceAsync(
            WorkspaceId,
            replacements,
            new RestructureScope(FilePath: null, ProjectName: null),
            CancellationToken.None);

        Assert.AreEqual(0, preview.Changes.Count, "Empty preview must report zero changes.");
        Assert.AreEqual(string.Empty, preview.PreviewToken,
            "Empty preview must use an empty token — there is nothing to redeem.");
        StringAssert.Contains(preview.Description, "No matching string literals",
            "Description must explain why the preview is empty.");
        Assert.IsNull(preview.Warnings);
    }

    [TestMethod]
    public async Task PreviewReplace_EmptyReplacementList_StillThrowsArgumentException()
    {
        // Guard that the zero-match relaxation didn't weaken input validation. A caller
        // passing an empty replacements array is a programming error, distinct from a
        // well-formed replacement that simply doesn't match anything in scope.
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            Service.PreviewReplaceAsync(
                WorkspaceId,
                replacements: Array.Empty<StringLiteralReplacementDto>(),
                new RestructureScope(FilePath: null, ProjectName: null),
                CancellationToken.None));
    }
}
