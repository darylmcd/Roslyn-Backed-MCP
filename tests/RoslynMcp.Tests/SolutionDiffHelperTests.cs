using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class SolutionDiffHelperTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectReferenceReplacement_IncludesPhysicalProjectDiffAndMetadata(bool replaceBothContexts)
    {
        var root = Path.Combine(Path.GetTempPath(), "reference-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "A.csproj");
            await File.WriteAllTextAsync(projectPath,
                "<Project><ItemGroup><ProjectReference Include=\"B.csproj\" Condition=\"'$(TargetFramework)' == 'net10.0'\"><Aliases>old</Aliases></ProjectReference>"
                + "<ProjectReference Include=\"B.csproj\" Condition=\"'$(TargetFramework)' == 'net9.0'\"><Aliases>other</Aliases></ProjectReference></ItemGroup></Project>");
            using var workspace = new AdhocWorkspace();
            var a = ProjectId.CreateNewId();
            var b = ProjectId.CreateNewId();
            var oldReference = new ProjectReference(b, ["old"]);
            var otherContext = ProjectId.CreateNewId();
            var original = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(a, VersionStamp.Create(), "A", "A", LanguageNames.CSharp, filePath: projectPath))
                .AddProject(ProjectInfo.Create(b, VersionStamp.Create(), "B", "B", LanguageNames.CSharp, filePath: Path.Combine(root, "B.csproj")))
                .AddProjectReference(a, oldReference);
            if (replaceBothContexts)
                original = original.AddProject(ProjectInfo.Create(otherContext, VersionStamp.Create(), "A-net9", "A", LanguageNames.CSharp, filePath: projectPath))
                    .AddProjectReference(otherContext, new ProjectReference(b, ["other"]));
            var modified = original.RemoveProjectReference(a, oldReference)
                .AddProjectReference(a, new ProjectReference(b, ["reviewed"], embedInteropTypes: true));
            if (replaceBothContexts)
                modified = modified.RemoveProjectReference(otherContext, new ProjectReference(b, ["other"]))
                    .AddProjectReference(otherContext, new ProjectReference(b, ["other-reviewed"]));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, CancellationToken.None);
            var change = changes.Single(item => item.FilePath == projectPath);
            StringAssert.Contains(change.UnifiedDiff, "reviewed");
            StringAssert.Contains(change.UnifiedDiff, "EmbedInteropTypes");
            var mutation = (await RoslynMcp.Roslyn.Services.ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
            var rendered = System.Xml.Linq.XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content);
            Assert.AreEqual(replaceBothContexts ? "other-reviewed" : "other", rendered.Descendants("ProjectReference")
                .Single(element => ((string?)element.Attribute("Condition"))!.Contains("net9.0", StringComparison.Ordinal))
                .Element("Aliases")!.Value, "A metadata replacement must not alter another framework's reference.");
            StringAssert.Contains(await File.ReadAllTextAsync(projectPath), "old");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AddedCompilationReference_PreservesExistingBuildOnlyReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "build-only-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "A.csproj");
            await File.WriteAllTextAsync(path,
                "<Project><ItemGroup><ProjectReference Include=\"B.csproj\"><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference></ItemGroup></Project>");
            using var workspace = new AdhocWorkspace();
            var a = ProjectId.CreateNewId();
            var b = ProjectId.CreateNewId();
            var original = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(a, VersionStamp.Create(), "A", "A", LanguageNames.CSharp, filePath: path))
                .AddProject(ProjectInfo.Create(b, VersionStamp.Create(), "B", "B", LanguageNames.CSharp, filePath: Path.Combine(root, "B.csproj")));
            var modified = original.AddProjectReference(a, new ProjectReference(b));
            var mutation = (await RoslynMcp.Roslyn.Services.ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
            var rendered = System.Xml.Linq.XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content);
            Assert.HasCount(2, rendered.Descendants("ProjectReference").ToArray());
            Assert.HasCount(1, rendered.Descendants("ProjectReference").Where(element => element.Element("ReferenceOutputAssembly")?.Value == "false").ToArray());
            Assert.HasCount(1, rendered.Descendants("ProjectReference").Where(element => element.Element("ReferenceOutputAssembly") is null).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (Solution solution, ProjectId projectId) CreateEmptySolution()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp);
        return (solution, projectId);
    }

    [TestMethod]
    public async Task ChangedDocument_ReturnsOneChangeWithCorrectFilePath()
    {
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        var oldSolution = solution
            .AddDocument(docId, "Test.cs", SourceText.From("class A { }"));
        var newSolution = oldSolution
            .WithDocumentText(docId, SourceText.From("class B { }"));

        var changes = await SolutionDiffHelper.ComputeChangesAsync(oldSolution, newSolution, CancellationToken.None);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "Test.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task AddedDocument_ReturnsChange()
    {
        var (solution, projectId) = CreateEmptySolution();
        var oldSolution = solution;
        var docId = DocumentId.CreateNewId(projectId);
        var newSolution = solution
            .AddDocument(docId, "New.cs", SourceText.From("class New { }"));

        var changes = await SolutionDiffHelper.ComputeChangesAsync(oldSolution, newSolution, CancellationToken.None);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "New.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task RemovedDocument_ReturnsChange()
    {
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        var oldSolution = solution
            .AddDocument(docId, "Remove.cs", SourceText.From("class Remove { }"));
        var newSolution = oldSolution.RemoveDocument(docId);

        var changes = await SolutionDiffHelper.ComputeChangesAsync(oldSolution, newSolution, CancellationToken.None);

        Assert.AreEqual(1, changes.Count);
        StringAssert.EndsWith(changes[0].FilePath, "Remove.cs");
        Assert.IsFalse(string.IsNullOrWhiteSpace(changes[0].UnifiedDiff));
    }

    [TestMethod]
    public async Task NoChanges_ReturnsEmptyList()
    {
        var (solution, _) = CreateEmptySolution();

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, solution, CancellationToken.None);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public async Task IdenticalText_ReturnsEmptyList()
    {
        // Use the same document ID but replace with an identical SourceText object.
        // Roslyn reports this as a "changed" document, but the text is identical,
        // so SolutionDiffHelper should skip it.
        var (solution, projectId) = CreateEmptySolution();
        var docId = DocumentId.CreateNewId(projectId);
        const string content = "class Same { }";

        var oldSolution = solution
            .AddDocument(docId, "Same.cs", SourceText.From(content));
        var newSolution = oldSolution
            .WithDocumentText(docId, SourceText.From(content));

        var changes = await SolutionDiffHelper.ComputeChangesAsync(oldSolution, newSolution, CancellationToken.None);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public async Task Truncation_LastEntryHasTruncatedFilePath()
    {
        var (solution, projectId) = CreateEmptySolution();
        var oldSolution = solution;
        var newSolution = solution;

        // Generate a large block of text so each document diff is substantial.
        // DefaultMaxTotalChars is 64 * 1024 = 65536 chars.
        // Create enough documents with large content to exceed the limit.
        var largeContent = new string('x', 4096);
        for (int i = 0; i < 30; i++)
        {
            var docId = DocumentId.CreateNewId(projectId);
            newSolution = newSolution
                .AddDocument(docId, $"Big{i}.cs", SourceText.From($"// file {i}\n{largeContent}"));
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(oldSolution, newSolution, CancellationToken.None);

        Assert.IsTrue(changes.Count >= 2, "Expected at least 2 entries (some diffs + truncation marker).");
        var last = changes[changes.Count - 1];
        Assert.AreEqual("<truncated>", last.FilePath);
    }
}
