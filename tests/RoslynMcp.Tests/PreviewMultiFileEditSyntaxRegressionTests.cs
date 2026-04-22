using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression: <c>preview_multi_file_edit</c> with default <c>skipSyntaxCheck=false</c> must
/// not emit a clean preview for parser-recovery shapes (Jellyfin F17: file-scoped namespace,
/// line comment, stray brace) that previously passed <c>GetCSharpSyntaxErrors</c> when the
/// tree only surfaced non-Error severities or skipped text without a severed Error diagnostic.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class PreviewMultiFileEditSyntaxRegressionTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task PreviewMultiFile_F17NamespaceCommentBrace_ThrowsWhenSyntaxCheckEnabled()
    {
        var edit = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance,
            UndoService,
            ChangeTracker,
            PreviewStore,
            CompileCheckService);
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var canonicalDog = Path.Combine(RepositoryRootPath, "samples", "SampleSolution", "SampleLib", "Dog.cs");
        var firstLine = (await File.ReadAllTextAsync(canonicalDog, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        var fileEdits = new[]
        {
            new FileEditsDto(
                dogFilePath,
                new[] { new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n") }),
        };

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await edit.PreviewMultiFileTextEditsAsync(
                workspaceId, fileEdits, CancellationToken.None, skipSyntaxCheck: false);
        });
        StringAssert.Contains(ex.Message, "preview_multi_file_edit", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "syntax", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task PreviewMultiFile_F17Shape_SkipSyntaxCheck_StillProducesPreview()
    {
        var edit = new EditService(
            WorkspaceManager,
            NullLogger<EditService>.Instance,
            UndoService,
            ChangeTracker,
            PreviewStore,
            CompileCheckService);
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var canonicalDog = Path.Combine(RepositoryRootPath, "samples", "SampleSolution", "SampleLib", "Dog.cs");
        var firstLine = (await File.ReadAllTextAsync(canonicalDog, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        var fileEdits = new[]
        {
            new FileEditsDto(
                dogFilePath,
                new[] { new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n") }),
        };

        var dto = await edit.PreviewMultiFileTextEditsAsync(
            workspaceId, fileEdits, CancellationToken.None, skipSyntaxCheck: true);

        Assert.IsFalse(string.IsNullOrEmpty(dto.PreviewToken));
        Assert.IsTrue(dto.Changes?.Count > 0);
    }
}
