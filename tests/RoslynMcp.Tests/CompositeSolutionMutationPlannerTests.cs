using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompositeSolutionMutationPlannerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddedLinkToBaselinePath_OnlyWritesWithMatchingEdit(bool withEdit)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, firstProject, secondProject) = CreateProjects(workspace);
        var path = NewFilePath();
        var originalId = DocumentId.CreateNewId(firstProject);
        var baseline = solution.AddDocument(originalId, "Linked.cs", SourceText.From("original"), filePath: path);
        var content = withEdit ? "changed" : "original";
        var pending = AddDocument(baseline, secondProject, path, content);
        if (withEdit)
            pending = pending.WithDocumentText(originalId, SourceText.From(content));

        var mutations = await CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None);

        if (withEdit)
        {
            Assert.AreEqual(new CompositeFileMutation(path, content, CompositeFileMutationMode.Edit), mutations.Single().Mutation);
        }
        else
        {
            Assert.IsEmpty(mutations);
        }
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "added-only divergent link")]
    [DataRow(true, false, DisplayName = "explicit generated file mapped by stale baseline")]
    [DataRow(true, true, DisplayName = "explicit generated file mapped by stale baseline with a new occupant")]
    public async Task AddedDocumentCannotAuthorizeOverwriteOfBaselinePath(bool explicitCreate, bool competitorExists)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, firstProject, secondProject) = CreateProjects(workspace);
        var path = NewFilePath();
        var baseline = AddDocument(solution, firstProject, path, "original");
        var addedId = DocumentId.CreateNewId(secondProject);
        var pending = baseline.AddDocument(addedId, "Linked.cs", SourceText.From("generated"), filePath: path);
        var directory = Path.GetDirectoryName(path)!;

        try
        {
            if (competitorExists)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(path, "competing creator");
            }

            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, explicitCreate ? [addedId] : [], CancellationToken.None));
            if (competitorExists)
                Assert.AreEqual("competing creator", await File.ReadAllTextAsync(path));
            else
                Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(directory);
        }
    }

    [TestMethod]
    public async Task IdenticalLinkedAdditions_PlanOneExclusiveCreate()
    {
        using var workspace = new AdhocWorkspace();
        var (baseline, firstProject, secondProject) = CreateProjects(workspace);
        var path = NewFilePath();
        var pending = AddDocument(AddDocument(baseline, firstProject, path, "generated"), secondProject, path, "generated");

        var mutations = await CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None);

        Assert.AreEqual(new CompositeFileMutation(path, "generated", CompositeFileMutationMode.Create), mutations.Single().Mutation);
    }

    [TestMethod]
    public async Task DivergentLinkedAdditions_AreRefused()
    {
        using var workspace = new AdhocWorkspace();
        var (baseline, firstProject, secondProject) = CreateProjects(workspace);
        var path = NewFilePath();
        var pending = AddDocument(AddDocument(baseline, firstProject, path, "first"), secondProject, path, "second");

        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "single changed owner in agreement with the baseline")]
    [DataRow(true, false, DisplayName = "divergent original owners")]
    [DataRow(false, true, DisplayName = "divergent independently changed owners")]
    public async Task LinkedEdits_ReconcileEveryBaselineAndSurvivingOwner(bool baselineDiverges, bool finalDiverges)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, firstProject, secondProject) = CreateProjects(workspace);
        var path = NewFilePath();
        var firstId = DocumentId.CreateNewId(firstProject);
        var secondId = DocumentId.CreateNewId(secondProject);
        var baseline = solution
            .AddDocument(firstId, "Linked.cs", SourceText.From("original"), filePath: path)
            .AddDocument(secondId, "Linked.cs", SourceText.From(baselineDiverges ? "other baseline" : "original"), filePath: path);
        var pending = baseline.WithDocumentText(firstId, SourceText.From("changed"));
        if (finalDiverges)
            pending = pending.WithDocumentText(secondId, SourceText.From("other write"));

        if (baselineDiverges || finalDiverges)
        {
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None));
        }
        else
        {
            var edits = await CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None);
            Assert.AreEqual("original", edits.Single().OriginalContent);
            Assert.AreEqual(new CompositeFileMutation(path, "changed", CompositeFileMutationMode.Edit), edits.Single().Mutation);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyFileCreateAndDelete_AppearInReview(bool delete)
    {
        using var workspace = new AdhocWorkspace();
        var (withoutFile, project, _) = CreateProjects(workspace);
        var path = NewFilePath();
        var withFile = AddDocument(withoutFile, project, path, string.Empty);
        var edits = await CompositeSolutionMutationPlanner.BuildAsync(
            delete ? withFile : withoutFile, delete ? withoutFile : withFile, [], CancellationToken.None);

        var change = CompositeSolutionMutationPlanner.RenderReview(edits, CancellationToken.None).Single();
        Assert.AreEqual(path, change.FilePath);
        Assert.AreEqual(delete ? $"--- a/{path}{Environment.NewLine}+++ /dev/null{Environment.NewLine}" :
            $"--- /dev/null{Environment.NewLine}+++ b/{path}{Environment.NewLine}", change.UnifiedDiff);
        Assert.AreEqual(delete ? CompositeFileMutationMode.Delete : CompositeFileMutationMode.Create, edits.Single().Mutation.Mode);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "create")]
    [DataRow(true, DisplayName = "edit")]
    public async Task CompositePlan_RejectsParentTraversalBeforeNormalizingItsDestination(bool existing)
    {
        using var workspace = new AdhocWorkspace();
        var (solution, project, _) = CreateProjects(workspace);
        var path = NewFilePath();
        var rawPath = Path.Combine(Path.GetDirectoryName(path)!, "alias", "..", Path.GetFileName(path));
        var id = DocumentId.CreateNewId(project);
        var baseline = existing
            ? solution.AddDocument(id, "Linked.cs", SourceText.From("original"), filePath: rawPath)
            : solution;
        var pending = existing
            ? baseline.WithDocumentText(id, SourceText.From("changed"))
            : baseline.AddDocument(id, "Linked.cs", SourceText.From("changed"), filePath: rawPath);
        Assert.AreEqual(rawPath, pending.GetDocument(id)!.FilePath);
        Assert.AreEqual(path, Path.GetFullPath(rawPath));

        await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
            CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task CompositePlan_ValidatesAlternateSeparatorsBeforePublishingCanonicalSpelling()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, project, _) = CreateProjects(workspace);
        var path = NewFilePath();
        var rawPath = path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var id = DocumentId.CreateNewId(project);
        var baseline = solution.AddDocument(id, "Linked.cs", SourceText.From("original"), filePath: rawPath);
        var pending = baseline.WithDocumentText(id, SourceText.From("changed"));

        var edit = (await CompositeSolutionMutationPlanner.BuildAsync(baseline, pending, [], CancellationToken.None)).Single();

        Assert.AreEqual(path, edit.Mutation.FilePath);
        WorkspacePhysicalPathGuard.EnsurePhysicalPath(edit.Mutation.FilePath);
        Assert.AreEqual(path, CompositeSolutionMutationPlanner.RenderReview([edit], CancellationToken.None).Single().FilePath);
    }

    private static (Solution Solution, ProjectId FirstProject, ProjectId SecondProject) CreateProjects(AdhocWorkspace workspace)
    {
        var firstProject = ProjectId.CreateNewId();
        var secondProject = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(firstProject, "First", "First", LanguageNames.CSharp)
            .AddProject(secondProject, "Second", "Second", LanguageNames.CSharp);
        return (solution, firstProject, secondProject);
    }

    private static Solution AddDocument(Solution solution, ProjectId project, string path, string content) =>
        solution.AddDocument(DocumentId.CreateNewId(project), Path.GetFileName(path), SourceText.From(content), filePath: path);

    private static string NewFilePath() =>
        Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"), "Linked.cs");
}
