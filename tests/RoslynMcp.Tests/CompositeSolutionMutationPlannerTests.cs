using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
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
            Assert.AreEqual(new CompositeFileMutation(path, content, CompositeFileMutationMode.Edit), mutations.Single());
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

        Assert.AreEqual(new CompositeFileMutation(path, "generated", CompositeFileMutationMode.Create), mutations.Single());
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
