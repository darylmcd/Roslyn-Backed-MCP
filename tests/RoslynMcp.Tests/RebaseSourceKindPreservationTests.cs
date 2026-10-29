using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// refactoring-rebase-source-kind-preservation: the reviewed <see cref="SourceCodeKind"/> is part of a
/// preview's intent. The sibling-rebase replay and the move-file document clone must carry it.
/// Kinds are set synthetically through the Solution API; no native .csx loading is claimed.
/// </summary>
[TestClass]
public sealed class RebaseSourceKindPreservationTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task Sibling_Rebase_Preserves_Reviewed_Kind_On_Changed_Document()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var target = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "AnimalService.cs"))!;
        var sibling = SymbolResolver.FindDocument(original, workspace.GetPath("SampleApp", "Program.cs"))!;
        var reviewedText = SourceText.From("// reviewed\n", new UTF8Encoding(true));

        var modified = original.WithDocumentSourceCodeKind(target.Id, SourceCodeKind.Script)
            .WithDocumentText(target.Id, reviewedText);
        var current = original.WithDocumentText(sibling.Id, SourceText.From("// sibling apply\n"));
        Assert.IsFalse(ReferenceEquals(original, current));

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        var rebasedTarget = rebased.GetDocument(target.Id)!;
        Assert.AreEqual(SourceCodeKind.Script, rebasedTarget.SourceCodeKind);
        var text = await rebasedTarget.GetTextAsync();
        Assert.AreEqual("// reviewed\n", text.ToString());
        Assert.AreEqual(reviewedText.Encoding?.WebName, text.Encoding?.WebName);
        Assert.AreEqual(target.Project.Id, rebasedTarget.Project.Id);
        Assert.AreEqual(target.FilePath, rebasedTarget.FilePath);
        Assert.AreEqual("// sibling apply\n", (await rebased.GetDocument(sibling.Id)!.GetTextAsync()).ToString());
        Assert.AreEqual(SourceCodeKind.Regular, rebased.GetDocument(sibling.Id)!.SourceCodeKind);
    }

    [TestMethod]
    public async Task Sibling_Rebase_Preserves_Reviewed_Kind_On_Added_Document()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sibling = SymbolResolver.FindDocument(original, workspace.GetPath("SampleApp", "Program.cs"))!;
        var project = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "AnimalService.cs"))!.Project;
        var addedPath = workspace.GetPath("SampleLib", "Generated", "Reviewed.csx");
        var addedText = SourceText.From("var reviewed = 1;\n", new UTF8Encoding(true));
        var addedId = DocumentId.CreateNewId(project.Id);

        var modified = original.AddDocument(DocumentInfo.Create(
            addedId,
            "Reviewed.csx",
            folders: ["Generated"],
            sourceCodeKind: SourceCodeKind.Script,
            loader: TextLoader.From(TextAndVersion.Create(addedText, VersionStamp.Create())),
            filePath: addedPath));
        var current = original.WithDocumentText(sibling.Id, SourceText.From("// sibling apply\n"));

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        var added = SymbolResolver.FindDocument(rebased, addedPath);
        Assert.IsNotNull(added, "The reviewed addition must be mirrored into the rebased solution.");
        Assert.AreEqual(SourceCodeKind.Script, added.SourceCodeKind);
        Assert.AreEqual(project.Id, added.Project.Id);
        CollectionAssert.AreEqual(new[] { "Generated" }, added.Folders.ToArray());
        var text = await added.GetTextAsync();
        Assert.AreEqual("var reviewed = 1;\n", text.ToString());
        Assert.AreEqual(addedText.Encoding?.WebName, text.Encoding?.WebName);
        Assert.AreEqual("// sibling apply\n", (await rebased.GetDocument(sibling.Id)!.GetTextAsync()).ToString(),
            "The sibling's edit must survive the replay.");
    }

    [TestMethod]
    public async Task Move_File_Preview_Preserves_Source_Document_Kind()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var real = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sourcePath = workspace.GetPath("SampleLib", "AnimalService.cs");
        var destinationPath = workspace.GetPath("SampleLib", "Moved", "AnimalService.cs");
        var source = SymbolResolver.FindDocument(real, sourcePath)!;
        // Test-only seam: the workspace manager is stubbed so the producer observes a loaded
        // document whose kind is Script (MSBuildWorkspace never assigns Script to a .cs Compile item).
        var scripted = real.WithDocumentSourceCodeKind(source.Id, SourceCodeKind.Script);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var stub = new FailClosedWorkspaceManagerStub
        {
            GetCurrentSolutionHandler = _ => scripted,
            GetCurrentVersionHandler = _ => version,
        };
        var service = new FileOperationService(stub, PreviewStore, NullLogger<FileOperationService>.Instance);

        var preview = await service.PreviewMoveFileAsync(
            workspace.WorkspaceId,
            new MoveFileDto(sourcePath, destinationPath, null, false),
            CancellationToken.None);

        var stored = PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        var moved = SymbolResolver.FindDocument(stored.Value.ModifiedSolution, destinationPath);
        Assert.IsNotNull(moved);
        Assert.AreEqual(SourceCodeKind.Script, moved.SourceCodeKind);
        Assert.AreEqual(source.Project.Id, moved.Project.Id);
        Assert.AreEqual(
            (await source.GetTextAsync()).ToString(),
            (await moved.GetTextAsync()).ToString());
        Assert.IsNull(SymbolResolver.FindDocument(stored.Value.ModifiedSolution, sourcePath));
    }
}
