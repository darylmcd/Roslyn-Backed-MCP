using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// preview-rebase-linked-document-project-identity: a preview's document identity is
/// (project, document), not the physical path. When the same file is a document in two projects
/// (linked item), the sibling-rebase replay must apply the reviewed removal/edit to the project the
/// preview named and leave the other project's linked copy alone.
/// </summary>
[TestClass]
public sealed class RebaseDocumentIdentityTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// Builds a solution where <c>earlier</c> (first in iteration order) holds a linked copy of a
    /// document owned by <c>reviewed</c> (a later project): same <see cref="Document.FilePath"/>.
    /// </summary>
    private sealed record LinkedFixture(
        Solution Original,
        Document ReviewedDocument,
        Document LinkedCopy,
        Document UnrelatedDocument);

    private static LinkedFixture BuildLinkedFixture(Solution baseline)
    {
        var projects = baseline.Projects.ToList();
        Assert.IsTrue(projects.Count >= 2, "The sample solution must contain at least two projects.");
        var earlier = projects[0];
        var reviewed = projects[1];

        var reviewedDocument = reviewed.Documents.First(d =>
            d.FilePath is not null
            && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        var linkedId = DocumentId.CreateNewId(earlier.Id, "linked");
        var linkedText = SourceText.From("// linked copy in the earlier project\n");
        var withLink = baseline.AddDocument(DocumentInfo.Create(
            linkedId,
            reviewedDocument.Name,
            loader: TextLoader.From(TextAndVersion.Create(linkedText, VersionStamp.Create())),
            filePath: reviewedDocument.FilePath));

        var unrelated = withLink.GetProject(earlier.Id)!.Documents.First(d =>
            d.Id != linkedId && d.FilePath is not null && !string.Equals(d.FilePath, reviewedDocument.FilePath, StringComparison.OrdinalIgnoreCase));

        // Precondition that makes the old first-path-match wrong: the linked copy is hit first.
        var firstPathHit = withLink.Projects.SelectMany(p => p.Documents).First(d =>
            string.Equals(d.FilePath, reviewedDocument.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(linkedId, firstPathHit.Id, "Fixture precondition: the linked copy must precede the reviewed document in iteration order.");

        return new LinkedFixture(
            withLink,
            withLink.GetDocument(reviewedDocument.Id)!,
            withLink.GetDocument(linkedId)!,
            unrelated);
    }

    [TestMethod]
    public async Task Removal_Rebase_Removes_Only_Reviewed_Project_Linked_Document()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixture = BuildLinkedFixture(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var original = fixture.Original;

        var modified = original.RemoveDocument(fixture.ReviewedDocument.Id);
        var current = original.WithDocumentText(fixture.UnrelatedDocument.Id, SourceText.From("// sibling apply\n"));
        Assert.IsFalse(ReferenceEquals(original, current));

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        Assert.IsNull(rebased.GetDocument(fixture.ReviewedDocument.Id),
            "The reviewed project's document must be removed.");
        var linked = rebased.GetDocument(fixture.LinkedCopy.Id);
        Assert.IsNotNull(linked, "The linked copy in the other project must survive the removal.");
        Assert.AreEqual(fixture.ReviewedDocument.FilePath, linked.FilePath);
        Assert.AreEqual("// sibling apply\n",
            (await rebased.GetDocument(fixture.UnrelatedDocument.Id)!.GetTextAsync()).ToString(),
            "The sibling's unrelated edit must survive the replay.");
    }

    [TestMethod]
    public async Task Changed_Rebase_Fallback_Targets_Reviewed_Project_After_Id_Lineage_Change()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var fixture = BuildLinkedFixture(WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        var original = fixture.Original;
        var reviewedText = SourceText.From("// reviewed edit\n");
        var linkedBefore = (await fixture.LinkedCopy.GetTextAsync()).ToString();

        var modified = original.WithDocumentText(fixture.ReviewedDocument.Id, reviewedText);

        // Current: the reviewed project's document was re-created under a NEW DocumentId at the same
        // path (reload-style lineage change), forcing the DocumentId lookup to miss.
        var reviewedProjectId = fixture.ReviewedDocument.Project.Id;
        var replacementId = DocumentId.CreateNewId(reviewedProjectId, "replacement");
        var originalText = await fixture.ReviewedDocument.GetTextAsync();
        var current = original.RemoveDocument(fixture.ReviewedDocument.Id)
            .AddDocument(DocumentInfo.Create(
                replacementId,
                fixture.ReviewedDocument.Name,
                loader: TextLoader.From(TextAndVersion.Create(originalText, VersionStamp.Create())),
                filePath: fixture.ReviewedDocument.FilePath));

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        Assert.AreEqual("// reviewed edit\n",
            (await rebased.GetDocument(replacementId)!.GetTextAsync()).ToString(),
            "The reviewed edit must land in the reviewed project's document.");
        Assert.AreEqual(linkedBefore,
            (await rebased.GetDocument(fixture.LinkedCopy.Id)!.GetTextAsync()).ToString(),
            "The other project's linked copy must not receive the edit.");
    }

    /// <summary>
    /// End to end through <c>ApplyRefactoringAsync</c> with a real MSBuild linked item: the earlier
    /// project links a file physically owned by the later project. A removal previewed for the later
    /// project, redeemed after a sibling apply moved the workspace, must remove the later project's
    /// document and keep the earlier project's linked document; undo restores the removed document.
    /// Both documents share one physical file, and removal deletes that file on disk, so the
    /// assertions are on workspace solution state (plus the restored file after undo).
    /// </summary>
    [TestMethod]
    public async Task Linked_Document_Removal_Apply_After_Sibling_Apply_Keeps_Other_Project_Copy_And_Undo_Restores()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var initial = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var projects = initial.Projects.ToList();
        var earlier = projects[0];
        var later = projects[1];
        var sharedPath = later.Documents.First(d =>
            d.FilePath is not null
            && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)).FilePath!;

        // Real MSBuild linked item: the earlier project includes the later project's physical file.
        var csprojPath = earlier.FilePath!;
        var csproj = System.Xml.Linq.XDocument.Load(csprojPath);
        var relative = Path.GetRelativePath(Path.GetDirectoryName(csprojPath)!, sharedPath);
        csproj.Root!.Add(new System.Xml.Linq.XElement("ItemGroup",
            new System.Xml.Linq.XElement("Compile",
                new System.Xml.Linq.XAttribute("Include", relative),
                new System.Xml.Linq.XAttribute("Link", "Linked/" + Path.GetFileName(sharedPath)))));
        csproj.Save(csprojPath);
        await workspace.ReloadAsync(CancellationToken.None);

        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var earlierProject = original.Projects.Single(p => p.Name == earlier.Name);
        var laterProject = original.Projects.Single(p => p.Name == later.Name);
        var laterDocument = laterProject.Documents.Single(d => FileSystemPath.Comparer.Equals(d.FilePath, sharedPath));
        Assert.IsTrue(earlierProject.Documents.Any(d => FileSystemPath.Comparer.Equals(d.FilePath, sharedPath)),
            "Fixture precondition: the earlier project must hold the linked document.");
        Assert.IsTrue(original.Projects.TakeWhile(p => p.Id != laterProject.Id).Any(p => p.Id == earlierProject.Id),
            "Fixture precondition: the linked copy's project precedes the reviewed project.");

        ChangeTracker.Clear(workspace.WorkspaceId);
        var removalToken = PreviewStore.Store(
            workspace.WorkspaceId,
            original.RemoveDocument(laterDocument.Id),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId),
            "Remove linked document from the later project");

        var siblingPath = Path.Combine(Path.GetDirectoryName(earlierProject.FilePath)!, "Generated", "RebaseSibling.cs");
        var siblingPreview = await FileOperationService.PreviewCreateFileAsync(
            workspace.WorkspaceId,
            new CreateFileDto(earlierProject.Name, siblingPath, "namespace Generated;\n\npublic sealed class RebaseSibling\n{\n}\n"),
            CancellationToken.None);
        var sibling = await RefactoringService.ApplyRefactoringAsync(siblingPreview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(sibling.Success, sibling.Error);

        var removal = await RefactoringService.ApplyRefactoringAsync(removalToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(removal.Success, removal.Error);

        var afterRemoval = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.IsFalse(
            afterRemoval.Projects.Single(p => p.Name == later.Name).Documents
                .Any(d => FileSystemPath.Comparer.Equals(d.FilePath, sharedPath)),
            "The reviewed (later) project's document must be removed.");
        Assert.IsTrue(
            afterRemoval.Projects.Single(p => p.Name == earlier.Name).Documents
                .Any(d => FileSystemPath.Comparer.Equals(d.FilePath, sharedPath)),
            "The earlier project's linked document must remain in the workspace.");
        Assert.IsNotNull(SymbolResolver.FindDocument(afterRemoval, siblingPath),
            "The sibling apply must survive the replay.");

        var changes = ChangeTracker.GetChanges(workspace.WorkspaceId);
        var removalChange = changes[^1];
        var undo = await UndoService.RevertBySequenceAsync(
            workspace.WorkspaceId, removalChange.SequenceNumber, CancellationToken.None);
        Assert.IsTrue(undo.Reverted, undo.Reason);

        var afterUndo = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.IsTrue(
            afterUndo.Projects.Single(p => p.Name == later.Name).Documents
                .Any(d => FileSystemPath.Comparer.Equals(d.FilePath, sharedPath)),
            "Undo must restore the removed document in the reviewed project.");
        Assert.IsTrue(File.Exists(sharedPath), "Undo must restore the shared physical file.");
    }
}
