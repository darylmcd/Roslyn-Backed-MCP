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
}
