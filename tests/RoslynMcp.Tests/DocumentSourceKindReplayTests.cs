using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DocumentSourceKindReplayTests
{
    [TestMethod]
    [DataRow("kind-only")]
    [DataRow("kind-and-text")]
    [DataRow("script-text")]
    [DataRow("script-relocate")]
    [DataRow("script-rename")]
    [DataRow("script-folders")]
    [DataRow("regular-reset")]
    [DataRow("regular-text")]
    public Task SiblingSnapshotRebase_PreservesReviewedSourceKindAndUnrelatedIntent(string shape)
        => WithSnapshotsAsync(async (baseline, reviewedId, siblingId, root) =>
        {
            var startsAsScript = shape.StartsWith("script-", StringComparison.Ordinal) || shape == "regular-reset";
            var original = baseline.WithDocumentSourceCodeKind(reviewedId, startsAsScript ? SourceCodeKind.Script : SourceCodeKind.Regular);
            var kind = shape is "regular-reset" or "regular-text" ? SourceCodeKind.Regular : SourceCodeKind.Script;
            var modified = original.WithDocumentSourceCodeKind(reviewedId, kind);
            if (shape is "kind-and-text" or "script-text" or "regular-text")
                modified = modified.WithDocumentText(reviewedId, SourceText.From("class ReviewedChange { }"));
            if (shape == "script-relocate")
                modified = modified.WithDocumentFilePath(reviewedId, Path.Combine(root, "Moved", "Moved.csx"))
                    .WithDocumentName(reviewedId, "Moved.csx").WithDocumentFolders(reviewedId, ["Moved"]);
            if (shape == "script-rename") modified = modified.WithDocumentName(reviewedId, "ReviewedName.csx");
            if (shape == "script-folders") modified = modified.WithDocumentFolders(reviewedId, ["ReviewedFolder"]);
            var current = original.WithDocumentText(siblingId, SourceText.From("class SiblingApplied { }"))
                .WithDocumentSourceCodeKind(siblingId, SourceCodeKind.Script);
            Assert.AreNotSame(original, current, "The sibling snapshot must exercise replay rather than the in-lineage fast path.");
            var delta = SolutionFileMutationPlanner.DocumentChanges(original, modified).Single();
            Assert.AreEqual(reviewedId, delta.New!.Id, "Kind-only intent must be present in the shared reviewed-delta inventory.");
            var reviewed = modified.GetDocument(reviewedId)!;
            Assert.AreEqual(kind, reviewed.SourceCodeKind);

            var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, modified, current, CancellationToken.None);

            var replayed = rebased.GetDocument(reviewedId);
            Assert.IsNotNull(replayed);
            Assert.AreEqual(kind, replayed.SourceCodeKind);
            Assert.AreEqual(kind, (await replayed.GetSyntaxTreeAsync())!.Options.Kind);
            Assert.AreEqual(reviewed.FilePath, replayed.FilePath);
            Assert.AreEqual(reviewed.Name, replayed.Name);
            CollectionAssert.AreEqual(reviewed.Folders.ToArray(), replayed.Folders.ToArray());
            Assert.AreEqual((await reviewed.GetTextAsync()).ToString(), (await replayed.GetTextAsync()).ToString());
            Assert.AreEqual("class SiblingApplied { }", (await rebased.GetDocument(siblingId)!.GetTextAsync()).ToString());
            Assert.AreEqual(SourceCodeKind.Script, rebased.GetDocument(siblingId)!.SourceCodeKind);
            Assert.AreEqual(original.GetDocument(reviewedId)!.SourceCodeKind, startsAsScript ? SourceCodeKind.Script : SourceCodeKind.Regular);
        });

    [TestMethod]
    public Task SiblingSnapshotRebase_PreservesNewlyAddedScriptDocument()
        => WithSnapshotsAsync(async (original, reviewedId, siblingId, root) =>
        {
            var projectId = original.GetDocument(reviewedId)!.Project.Id;
            var addedId = DocumentId.CreateNewId(projectId);
            var path = Path.Combine(root, "Added.csx");
            var modified = original.AddDocument(addedId, "Added.csx", SourceText.From("int added = 7;"), filePath: path)
                .WithDocumentSourceCodeKind(addedId, SourceCodeKind.Script);
            var current = original.WithDocumentText(siblingId, SourceText.From("class SiblingApplied { }"));
            Assert.IsNull(current.GetDocument(addedId));
            Assert.AreEqual(SourceCodeKind.Script, modified.GetDocument(addedId)!.SourceCodeKind);

            var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, modified, current, CancellationToken.None);

            var added = rebased.Projects.Single().Documents.Single(document => document.FilePath == path);
            Assert.AreEqual(SourceCodeKind.Script, added.SourceCodeKind);
            Assert.AreEqual(SourceCodeKind.Script, (await added.GetSyntaxTreeAsync())!.Options.Kind);
            Assert.AreEqual("int added = 7;", (await added.GetTextAsync()).ToString());
            Assert.AreEqual("class SiblingApplied { }", (await rebased.GetDocument(siblingId)!.GetTextAsync()).ToString());
            Assert.HasCount(3, rebased.Projects.Single().Documents);
        });

    [TestMethod]
    [DataRow(SourceCodeKind.Regular)]
    [DataRow(SourceCodeKind.Script)]
    public Task PhysicalComparison_AcceptsEqualPerDocumentSourceKinds(SourceCodeKind kind)
        => WithSnapshotsAsync(async (baseline, reviewedId, _, _) =>
        {
            var expected = baseline.WithDocumentSourceCodeKind(reviewedId, kind);
            var actual = expected.WithDocumentText(reviewedId, SourceText.From("class Reviewed { }"));
            Assert.AreEqual(kind, actual.GetDocument(reviewedId)!.SourceCodeKind);
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, static path => path,
                static path => path, true, BuildPathRelocation.KnownPathProperties, CancellationToken.None);
        });

    [TestMethod]
    [DataRow(SourceCodeKind.Regular, SourceCodeKind.Script)]
    [DataRow(SourceCodeKind.Script, SourceCodeKind.Regular)]
    public Task PhysicalComparison_RefusesChangedSourceKindBeforeAcceptingEqualText(SourceCodeKind before, SourceCodeKind after)
        => WithSnapshotsAsync(async (baseline, reviewedId, _, _) =>
        {
            var expected = baseline.WithDocumentSourceCodeKind(reviewedId, before);
            var actual = expected.WithDocumentSourceCodeKind(reviewedId, after);
            Assert.AreEqual(expected.GetDocument(reviewedId)!.Project.ParseOptions, actual.GetDocument(reviewedId)!.Project.ParseOptions);
            Assert.AreEqual((await expected.GetDocument(reviewedId)!.GetTextAsync()).ToString(),
                (await actual.GetDocument(reviewedId)!.GetTextAsync()).ToString());
            var failure = await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() =>
                SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, static path => path,
                    static path => path, true, BuildPathRelocation.KnownPathProperties, CancellationToken.None));
            StringAssert.Contains(failure.Message, "source document kind");
            // The explicitly graph-only contract remains graph-only.
            await SolutionFileMutationPlanner.AssertEquivalentAsync(expected, actual, static path => path,
                static path => path, false, BuildPathRelocation.KnownPathProperties, CancellationToken.None);
        });

    private static async Task WithSnapshotsAsync(Func<Solution, DocumentId, DocumentId, string, Task> run)
    {
        AdhocWorkspace? workspace = null;
        Exception? primary = null;
        try
        {
            workspace = new AdhocWorkspace();
            var root = Path.Combine(TestTempRoot.Current, nameof(DocumentSourceKindReplayTests), Guid.NewGuid().ToString("N"));
            var projectId = ProjectId.CreateNewId();
            var reviewedId = DocumentId.CreateNewId(projectId);
            var siblingId = DocumentId.CreateNewId(projectId);
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Owner", "Owner",
                LanguageNames.CSharp, filePath: Path.Combine(root, "Owner.csproj"), outputFilePath: Path.Combine(root, "Owner.dll"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), parseOptions: CSharpParseOptions.Default))
                .AddDocument(reviewedId, "Reviewed.csx", SourceText.From("class Reviewed { }"), filePath: Path.Combine(root, "Reviewed.csx"))
                .AddDocument(siblingId, "Sibling.cs", SourceText.From("class Sibling { }"), filePath: Path.Combine(root, "Sibling.cs"));
            await run(solution, reviewedId, siblingId, root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Failed to release source-kind replay snapshots.", primary, workspace); }
    }
}
