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

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
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

    // ------------------------------------------------------------------
    // edit-preview-validation-decomposition: both preview entry points now share one
    // SimulatePreviewAsync body. These cases pin the shared body's two observable behaviours —
    // syntax preflight and cross-file diff ordering — from BOTH call sites, so a future change to
    // the shared helper cannot silently diverge the public and internal surfaces.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task PreviewMultiFileOnSolution_F17NamespaceCommentBrace_ThrowsWhenSyntaxCheckEnabled()
    {
        var edit = NewEditService();
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");
        var fileEdits = new[] { new FileEditsDto(dogFilePath, new[] { await F17EditAsync(dogFilePath) }) };
        var solution = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            await edit.PreviewMultiFileTextEditsOnSolutionAsync(
                solution, fileEdits, CancellationToken.None, skipSyntaxCheck: false);
        });
        StringAssert.Contains(ex.Message, "preview_multi_file_edit", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "syntax", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task PreviewMultiFile_BothEntryPoints_ProduceIdenticalCrossFileDiffOrdering()
    {
        var edit = NewEditService();
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var workspaceId = workspace.WorkspaceId;
        var catFilePath = workspace.GetPath("SampleLib", "Cat.cs");
        var dogFilePath = workspace.GetPath("SampleLib", "Dog.cs");

        // Cat first, Dog second — the shared helper must emit diffs in the caller's input order,
        // not in path or document order.
        var fileEdits = new[]
        {
            new FileEditsDto(catFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// cat marker\n") }),
            new FileEditsDto(dogFilePath, new[] { new TextEditDto(1, 1, 1, 1, "// dog marker\n") }),
        };

        var viaSolution = await edit.PreviewMultiFileTextEditsOnSolutionAsync(
            WorkspaceManager.GetCurrentSolution(workspaceId), fileEdits, CancellationToken.None);
        var viaWorkspace = await edit.PreviewMultiFileTextEditsAsync(
            workspaceId, fileEdits, CancellationToken.None);

        CollectionAssert.AreEqual(
            viaSolution.Changes.Select(c => c.FilePath).ToArray(),
            viaWorkspace.Changes!.Select(c => c.FilePath).ToArray(),
            "Both preview entry points must report the same files in the same order.");
        CollectionAssert.AreEqual(
            viaSolution.Changes.Select(c => c.UnifiedDiff).ToArray(),
            viaWorkspace.Changes!.Select(c => c.UnifiedDiff).ToArray(),
            "Both preview entry points must produce byte-identical unified diffs.");
        Assert.AreEqual(catFilePath, viaWorkspace.Changes![0].FilePath, "Input order must be preserved.");
        Assert.AreEqual(viaSolution.Description, viaWorkspace.Description);
    }

    private static EditService NewEditService() => new(
        WorkspaceManager,
        NullLogger<EditService>.Instance,
        UndoService,
        ChangeTracker,
        PreviewStore,
        CompileCheckService);

    /// <summary>
    /// The Jellyfin F17 parser-recovery shape (file-scoped namespace + line comment + stray brace)
    /// appended to the end of line 1 of <paramref name="filePath"/>.
    /// </summary>
    private static async Task<TextEditDto> F17EditAsync(string filePath)
    {
        var firstLine = (await File.ReadAllTextAsync(filePath, CancellationToken.None))
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)[0];
        var colAfterLine1 = firstLine.Length + 1;
        return new TextEditDto(1, colAfterLine1, 1, colAfterLine1, "\n// F17\n{\n");
    }
}
