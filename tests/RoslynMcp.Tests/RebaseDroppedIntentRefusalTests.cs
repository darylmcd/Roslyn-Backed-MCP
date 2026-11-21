using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// rebase-changed-document-silent-drop (ADR 0043): a reviewed change whose target document or
/// project is gone from the current solution used to be skipped, so apply reported
/// <c>Success=true</c> with the edit lost. It is now refused with the existing invalid-preview
/// result in the DTO, before undo capture and persistence.
/// </summary>
[TestClass]
public sealed class RebaseDroppedIntentRefusalTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task Apply_Of_Edit_Whose_Document_Was_Deleted_By_Sibling_Is_Refused_Without_Effects()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var doomedPath = workspace.GetPath("SampleLib", "Dog.cs");
        var bystanderPath = workspace.GetPath("SampleLib", "Cat.cs");
        var doomed = SymbolResolver.FindDocument(original, doomedPath)!;
        var token = PreviewStore.Store(
            workspace.WorkspaceId,
            original.WithDocumentText(doomed.Id, SourceText.From((await doomed.GetTextAsync()).ToString() + "\n// reviewed edit\n")),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId),
            "Dropped intent regression");

        var delete = await FileOperationService.PreviewDeleteFileAsync(
            workspace.WorkspaceId, new DeleteFileDto(doomedPath), CancellationToken.None);
        var sibling = await RefactoringService.ApplyRefactoringAsync(delete.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(sibling.Success, sibling.Error);
        Assert.IsFalse(File.Exists(doomedPath));
        var bystanderBytes = await File.ReadAllBytesAsync(bystanderPath);
        var solutionBefore = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var versionBefore = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var changesBefore = ChangeTracker.GetChanges(workspace.WorkspaceId).Count;

        var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);

        var expected = await RefactoringService.ApplyRefactoringAsync("no-such-preview-token", "test_apply", CancellationToken.None);
        Assert.IsFalse(expected.Success);
        Assert.IsFalse(result.Success, "A reviewed edit whose document is gone must be refused, not reported as applied.");
        Assert.AreEqual(expected.Error, result.Error, "The refusal reuses the existing invalid-preview text.");
        Assert.HasCount(0, result.AppliedFiles);
        Assert.IsNull(result.MutatedSymbol);
        Assert.IsFalse(result.Error!.Contains(workspace.RootPath, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.Exists(doomedPath), "The refusal must not resurrect the deleted file.");
        CollectionAssert.AreEqual(bystanderBytes, await File.ReadAllBytesAsync(bystanderPath));
        Assert.AreSame(solutionBefore, WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(versionBefore, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.HasCount(changesBefore, ChangeTracker.GetChanges(workspace.WorkspaceId), "A refusal records no change.");
        var retry = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
        Assert.IsFalse(retry.Success);
        Assert.AreEqual(expected.Error, retry.Error, "The token is spent after a refusal.");
    }

    [TestMethod]
    public async Task Changed_Document_Gone_From_Current_Throws_Conflict()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var doc = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "Dog.cs"))!;
        var modified = original.WithDocumentText(doc.Id, SourceText.From("// edited\n"));
        var current = original.RemoveDocument(doc.Id);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Changed_Document_Project_Gone_From_Current_Throws_Conflict()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var doc = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "Dog.cs"))!;
        var modified = original.WithDocumentText(doc.Id, SourceText.From("// edited\n"));
        var current = original.RemoveProject(doc.Project.Id);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Added_Document_Without_File_Path_Throws_Conflict()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var modified = original.AddDocument(DocumentId.CreateNewId(project.Id), "NoPath.cs", SourceText.From("class NoPath {}\n"));
        var current = UnrelatedSiblingEdit(original, project);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Added_Document_Project_Gone_From_Current_Throws_Conflict()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = original.Projects.Single(p => p.Name == "SampleLib");
        var modified = original.AddDocument(
            DocumentId.CreateNewId(project.Id), "Added.cs", SourceText.From("class Added {}\n"),
            filePath: workspace.GetPath("SampleLib", "Added.cs"));
        var current = original.RemoveProject(project.Id);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Added_Project_Reference_Whose_Target_Is_Gone_Throws_Conflict()
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(includeContracts: false);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lib = original.Projects.Single(p => p.Name == "SampleLib");
        var contracts = original.Projects.Single(p => p.Name == "Contracts");
        var modified = original.AddProjectReference(lib.Id, new ProjectReference(contracts.Id));
        var current = UnrelatedSiblingEdit(original.RemoveProject(contracts.Id), lib);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Added_Project_Reference_On_Project_Gone_From_Current_Throws_Conflict()
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(includeContracts: false);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lib = original.Projects.Single(p => p.Name == "SampleLib");
        var contracts = original.Projects.Single(p => p.Name == "Contracts");
        var modified = original.AddProjectReference(lib.Id, new ProjectReference(contracts.Id));
        var current = original.RemoveProject(lib.Id);

        await AssertConflictAsync(original, modified, current);
    }

    [TestMethod]
    public async Task Removal_Of_Document_Already_Gone_Is_Satisfied_Not_Refused()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var doc = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "Dog.cs"))!;
        var modified = original.RemoveDocument(doc.Id);
        var current = original.RemoveDocument(doc.Id);

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        Assert.IsNull(rebased.GetDocument(doc.Id));
    }

    [TestMethod]
    public async Task Removed_Document_Without_File_Path_Is_Removed_By_Id()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var baseline = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var project = baseline.Projects.Single(p => p.Name == "SampleLib");
        var pathless = DocumentId.CreateNewId(project.Id);
        var original = baseline.AddDocument(pathless, "NoPath.cs", SourceText.From("class NoPath {}\n"));
        var modified = original.RemoveDocument(pathless);
        var current = UnrelatedSiblingEdit(original, original.GetProject(project.Id)!);

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        // Replay only; PersistRemovedDocuments still has no path to delete on disk (separate row).
        Assert.IsNull(rebased.GetDocument(pathless), "A reviewed removal of a path-less document must be replayed by id.");
    }

    [TestMethod]
    public async Task Removed_Only_Reference_To_Gone_Project_Is_Satisfied_Not_Refused()
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(includeContracts: true);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lib = original.Projects.Single(p => p.Name == "SampleLib");
        var contracts = original.Projects.Single(p => p.Name == "Contracts");
        var modified = original.WithProjectReferences(
            lib.Id, lib.ProjectReferences.Where(r => r.ProjectId != contracts.Id));
        var current = original.RemoveProject(contracts.Id);

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        Assert.IsNull(rebased.GetProject(contracts.Id));
    }

    [TestMethod]
    public async Task Unrelated_Dropped_Reference_Does_Not_Refuse_An_Affected_Reference_Change()
    {
        await using var workspace = await CreateReferenceWorkspaceAsync(includeContracts: true);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var lib = original.Projects.Single(p => p.Name == "SampleLib");
        var contracts = original.Projects.Single(p => p.Name == "Contracts");
        var other = original.Projects.Single(p => p.Name == "OtherContracts");
        var reviewedOther = new ProjectReference(other.Id, ["ReviewedAlias"], embedInteropTypes: false);
        var modified = original.WithProjectReferences(
            lib.Id, lib.ProjectReferences.Where(r => r.ProjectId != other.Id).Append(reviewedOther));
        var current = original.RemoveProject(contracts.Id);

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        var rebasedOther = rebased.GetProject(lib.Id)!.ProjectReferences.Single(r => r.ProjectId == other.Id);
        CollectionAssert.AreEqual(new[] { "ReviewedAlias" }, rebasedOther.Aliases.ToArray());
    }

    [TestMethod]
    public async Task Sibling_Edit_Of_Same_Document_Still_Replays_Last_Apply_Wins()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var doc = SymbolResolver.FindDocument(original, workspace.GetPath("SampleLib", "Dog.cs"))!;
        var modified = original.WithDocumentText(doc.Id, SourceText.From("// reviewed\n"));
        var current = original.WithDocumentText(doc.Id, SourceText.From("// sibling\n"));

        var rebased = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(
            original, modified, current, CancellationToken.None);

        Assert.AreEqual("// reviewed\n", (await rebased.GetDocument(doc.Id)!.GetTextAsync()).ToString());
    }

    private static Solution UnrelatedSiblingEdit(Solution solution, Project project)
    {
        var unrelated = solution.GetProject(project.Id)!.Documents.First(d => d.FilePath is not null);
        return solution.WithDocumentText(unrelated.Id, SourceText.From("// sibling apply\n"));
    }

    private static async Task AssertConflictAsync(Solution original, Solution modified, Solution current)
    {
        Assert.IsFalse(ReferenceEquals(original, current), "Fixture precondition: the workspace moved since the preview.");
        await Assert.ThrowsExactlyAsync<PreviewRebaseConflictException>(async () =>
            await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, modified, current, CancellationToken.None));
    }

    private static async Task<IsolatedWorkspaceScope> CreateReferenceWorkspaceAsync(bool includeContracts)
    {
        var workspace = CreateIsolatedWorkspaceCopy();
        return await InitializeWithCleanupAsync(workspace, async (scope, ct) =>
        {
            foreach (var name in new[] { "Contracts", "OtherContracts" })
            {
                AddProjectToCopiedSolution(scope.RootPath, name, "net10.0");
                await File.WriteAllTextAsync(scope.GetPath(name, "Marker.cs"), "namespace " + name + ";\npublic sealed class Marker { }\n", ct);
            }

            var projectPath = scope.GetPath("SampleLib", "SampleLib.csproj");
            var xml = System.Xml.Linq.XDocument.Load(projectPath);
            var references = new System.Xml.Linq.XElement("ItemGroup");
            references.Add(new System.Xml.Linq.XElement("ProjectReference",
                new System.Xml.Linq.XAttribute("Include", "../OtherContracts/OtherContracts.csproj")));
            if (includeContracts)
            {
                references.Add(new System.Xml.Linq.XElement("ProjectReference",
                    new System.Xml.Linq.XAttribute("Include", "../Contracts/Contracts.csproj")));
            }

            xml.Root!.Add(references);
            xml.Save(projectPath);
            await scope.LoadAsync(ct);
        });
    }
}
