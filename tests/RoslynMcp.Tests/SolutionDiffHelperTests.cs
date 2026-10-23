using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public class SolutionDiffHelperTests
{
    private static void AssertPublicRefusal(PublicInvalidOperationException refusal, string projectPath)
    {
        using var response = JsonDocument.Parse(ToolErrorHandler.ClassifyAndFormat(refusal, "refactor_preview"));
        var root = response.RootElement;
        Assert.AreEqual("InvalidOperation", root.GetProperty("category").GetString());
        Assert.AreEqual(refusal.PublicMessage, root.GetProperty("message").GetString());
        Assert.AreEqual(nameof(InvalidOperationException), root.GetProperty("exceptionType").GetString());
        Assert.IsFalse(refusal.PublicMessage.Contains(projectPath, StringComparison.Ordinal));
        Assert.IsFalse(refusal.PublicMessage.Contains(Path.GetDirectoryName(projectPath)!, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NewUnconditionalItem_RejectsAbsentOwnershipInSiblingContext(bool compileItem)
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup /></Project>");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, first);
        original = fixture.AddContext(original, second);
        var modified = compileItem
            ? original.AddDocument(DocumentId.CreateNewId(first), "Added.cs", SourceText.From("class Added { }"),
                filePath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "Added.cs"))
            : original.AddProjectReference(first, new ProjectReference(fixture.B));
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    public async Task DocumentAddition_DoesNotBindUnchangedAmbiguousReferenceMetadata()
    {
        using var fixture = new PhysicalReferenceFixture("<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>"
            + "<ProjectReference Include=\"B.csproj\"><Aliases Condition=\"first\">old</Aliases>"
            + "<Aliases Condition=\"second\">other</Aliases></ProjectReference></ItemGroup></Project>");
        var id = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, id, new ProjectReference(fixture.B, ["old"]));
        var modified = original.AddDocument(DocumentId.CreateNewId(id), "Added.cs", SourceText.From("class Added { }"),
            filePath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "Added.cs"));
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        Assert.HasCount(0, (await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddedCompileCannotReuseUnprovenConditionalItem(bool conditionOnGroup)
    {
        using var fixture = new PhysicalReferenceFixture(conditionOnGroup
            ? "<Project><ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0'\"><Compile Include=\"Shared.cs\" /></ItemGroup></Project>"
            : "<Project><ItemGroup><Compile Include=\"Shared.cs\" Condition=\"'$(TargetFramework)' == 'net10.0'\" /></ItemGroup></Project>");
        var existing = ProjectId.CreateNewId();
        var added = ProjectId.CreateNewId();
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "Shared.cs");
        var original = fixture.AddContext(fixture.Solution, existing);
        original = fixture.AddContext(original, added)
            .AddDocument(DocumentId.CreateNewId(existing), "Shared.cs", SourceText.From("class Shared { }"), filePath: path);
        var modified = original.AddDocument(DocumentId.CreateNewId(added), "Shared.cs", SourceText.From("class Shared { }"), filePath: path);
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    [DataRow("Aliases")]
    [DataRow("EmbedInteropTypes")]
    [DataRow("ReferenceOutputAssembly")]
    public async Task MultipleConditionalMetadataDefinitions_RejectAmbiguousReplacement(string metadataName)
    {
        var metadata = metadataName switch
        {
            "Aliases" => "<Aliases Condition=\"first\">old</Aliases><Aliases Condition=\"second\">other</Aliases>",
            "EmbedInteropTypes" => "<Aliases>old</Aliases><EmbedInteropTypes Condition=\"first\">false</EmbedInteropTypes><EmbedInteropTypes Condition=\"second\">true</EmbedInteropTypes>",
            _ => "<Aliases>old</Aliases><ReferenceOutputAssembly Condition=\"first\">true</ReferenceOutputAssembly><ReferenceOutputAssembly Condition=\"second\">false</ReferenceOutputAssembly>",
        };
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup><ProjectReference Include=\"B.csproj\">"
            + metadata + "</ProjectReference></ItemGroup></Project>");
        var id = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, id, new ProjectReference(fixture.B, ["old"]));
        var modified = original.WithProjectReferences(id, [new ProjectReference(fixture.B, ["new"], embedInteropTypes: true)]);
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddedReferenceCannotReuseUnprovenConditionalEdge(bool conditionOnGroup)
    {
        using var fixture = new PhysicalReferenceFixture(conditionOnGroup
            ? "<Project><ItemGroup Condition=\"'$(TargetFramework)' == 'net10.0'\"><ProjectReference Include=\"B.csproj\" /></ItemGroup></Project>"
            : "<Project><ItemGroup><ProjectReference Include=\"B.csproj\" Condition=\"'$(TargetFramework)' == 'net10.0'\" /></ItemGroup></Project>");
        var existing = ProjectId.CreateNewId();
        var added = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, existing, new ProjectReference(fixture.B));
        original = fixture.AddContext(original, added);
        var modified = original.AddProjectReference(added, new ProjectReference(fixture.B));
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task RetainedReferenceInSiblingContext_RejectsPhysicalRemovalOrReplacement(bool replace, bool changeUnrelatedDocument)
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup><ProjectReference Include=\"B.csproj\"><Aliases>shared</Aliases></ProjectReference></ItemGroup></Project>");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var reference = new ProjectReference(fixture.B, ["shared"]);
        var original = fixture.AddContext(fixture.Solution, first, reference);
        original = fixture.AddContext(original, second, reference);
        var unrelatedId = DocumentId.CreateNewId(second);
        original = original.AddDocument(unrelatedId, "Unrelated.cs", SourceText.From("class Before { }"),
            filePath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fixture.Path)!, "Unrelated.cs"));
        var modified = original.WithProjectReferences(first, replace ? [new ProjectReference(fixture.B, ["changed"])] : []);
        if (changeUnrelatedDocument)
            modified = modified.WithDocumentText(unrelatedId, SourceText.From("class After { }"));
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetainedExplicitCompileInSiblingContext_RejectsPhysicalRemoval(bool changeUnrelatedDocument)
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup><Compile Include=\"Shared.cs\" /></ItemGroup></Project>");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, first);
        original = fixture.AddContext(original, second);
        var directory = System.IO.Path.GetDirectoryName(fixture.Path)!;
        var firstId = DocumentId.CreateNewId(first);
        var secondId = DocumentId.CreateNewId(second);
        var unrelatedId = DocumentId.CreateNewId(second);
        original = original.AddDocument(firstId, "Shared.cs", SourceText.From("class Shared { }"), filePath: System.IO.Path.Combine(directory, "Shared.cs"))
            .AddDocument(secondId, "Shared.cs", SourceText.From("class Shared { }"), filePath: System.IO.Path.Combine(directory, "Shared.cs"))
            .AddDocument(unrelatedId, "Unrelated.cs", SourceText.From("class Before { }"), filePath: System.IO.Path.Combine(directory, "Unrelated.cs"));
        var modified = original.RemoveDocument(firstId);
        if (changeUnrelatedDocument)
            modified = modified.WithDocumentText(unrelatedId, SourceText.From("class After { }"));
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    public async Task ConditionalAliasSwap_BindsOriginalNodesRegardlessOfContextOrder()
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup>"
            + "<ProjectReference Include=\"B.csproj\" Condition=\"first\"><Aliases>left</Aliases></ProjectReference>"
            + "<ProjectReference Include=\"B.csproj\" Condition=\"second\"><Aliases>right</Aliases></ProjectReference>"
            + "</ItemGroup></Project>");
        byte[]? forwardBytes = null;
        foreach (var reverse in new[] { false, true })
        {
            var original = fixture.Solution;
            var first = ProjectId.CreateNewId();
            var second = ProjectId.CreateNewId();
            foreach (var id in reverse ? new[] { second, first } : new[] { first, second })
                original = fixture.AddContext(original, id, new ProjectReference(fixture.B, [id == first ? "left" : "right"]));
            var modified = original
                .WithProjectReferences(first, [new ProjectReference(fixture.B, ["right"])])
                .WithProjectReferences(second, [new ProjectReference(fixture.B, ["left"])]);

            var mutation = (await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
            var store = new PreviewStore();
            var token = store.Store("alias-swap", original, modified, 1, "swap aliases", false);
            CollectionAssert.AreEqual(new[] { fixture.Path }, store.PeekChangedPaths(token)!.ToArray(),
                "Several compilation contexts must authorize one physical project path.");
            var xml = XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content);
            Assert.AreEqual("right", xml.Descendants("ProjectReference").Single(item => (string?)item.Attribute("Condition") == "first").Element("Aliases")!.Value);
            Assert.AreEqual("left", xml.Descendants("ProjectReference").Single(item => (string?)item.Attribute("Condition") == "second").Element("Aliases")!.Value);
            if (forwardBytes is not null)
                CollectionAssert.AreEqual(forwardBytes, mutation.Modified, "Context enumeration must not affect physical bytes.");
            forwardBytes = mutation.Modified;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PartiallyOverlappingContexts_AgreeOnSharedOriginalNode(bool replace)
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup>"
            + "<ProjectReference Include=\"B.csproj\"><Aliases>shared</Aliases></ProjectReference>"
            + "<ProjectReference Include=\"C.csproj\"><Aliases>unique</Aliases></ProjectReference>"
            + "</ItemGroup></Project>");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, first,
            new ProjectReference(fixture.B, ["shared"]), new ProjectReference(fixture.C, ["unique"]));
        original = fixture.AddContext(original, second, new ProjectReference(fixture.B, ["shared"]));
        var modified = original.WithProjectReferences(first, replace
                ? [new ProjectReference(fixture.B, ["changed"]), new ProjectReference(fixture.C, ["changed-unique"])] : [])
            .WithProjectReferences(second, replace ? [new ProjectReference(fixture.B, ["changed"])] : []);

        var mutation = (await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
        var xml = XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content);
        CollectionAssert.AreEquivalent(replace ? new[] { "changed", "changed-unique" } : [],
            xml.Descendants("Aliases").Select(item => item.Value).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ContradictoryContexts_RejectBeforeChangingProjectBytes(bool removeSecond)
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup><ProjectReference Include=\"B.csproj\"><Aliases>old</Aliases></ProjectReference></ItemGroup></Project>");
        var first = ProjectId.CreateNewId();
        var second = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, first, new ProjectReference(fixture.B, ["old"]));
        original = fixture.AddContext(original, second, new ProjectReference(fixture.B, ["old"]));
        var modified = original.WithProjectReferences(first, [new ProjectReference(fixture.B, ["first"])])
            .WithProjectReferences(second, removeSecond ? [] : [new ProjectReference(fixture.B, ["second"])]);
        var bytes = await File.ReadAllBytesAsync(fixture.Path);

        var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None));
        AssertPublicRefusal(refusal, fixture.Path);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [TestMethod]
    public async Task MetadataReplacement_PreservesElementAttributesConditionsAndComments()
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup><ProjectReference Include=\"B.csproj\" Condition=\"reference-condition\">"
            + "<Aliases Condition=\"alias-condition\" Label=\"alias-label\">old<!--alias-note--></Aliases>"
            + "<EmbedInteropTypes Condition=\"embed-condition\" Label=\"embed-label\">false</EmbedInteropTypes>"
            + "<Private Label=\"untouched\">false</Private><!--reference-note--></ProjectReference></ItemGroup></Project>");
        var id = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, id, new ProjectReference(fixture.B, ["old"]));
        var modified = original.WithProjectReferences(id, [new ProjectReference(fixture.B, ["new"], embedInteropTypes: true)]);

        var mutation = (await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None)).Single();
        var reference = XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content).Descendants("ProjectReference").Single();
        Assert.AreEqual("reference-condition", (string?)reference.Attribute("Condition"));
        foreach (var (name, prefix, value) in new[] { ("Aliases", "alias", "new"), ("EmbedInteropTypes", "embed", "true") })
        {
            var metadata = reference.Element(name)!;
            Assert.AreEqual(value, metadata.Value);
            Assert.AreEqual(prefix + "-condition", (string?)metadata.Attribute("Condition"));
            Assert.AreEqual(prefix + "-label", (string?)metadata.Attribute("Label"));
        }
        Assert.AreEqual("false", reference.Element("Private")!.Value);
        Assert.AreEqual("untouched", (string?)reference.Element("Private")!.Attribute("Label"));
        CollectionAssert.AreEquivalent(new[] { "alias-note", "reference-note" }, reference.DescendantNodes().OfType<XComment>().Select(item => item.Value).ToArray());
    }

    [TestMethod]
    public async Task DuplicateTargetIds_AddOnePhysicalReferenceAndOneTokenWritePath()
    {
        using var fixture = new PhysicalReferenceFixture("<Project><ItemGroup /></Project>");
        var id = ProjectId.CreateNewId();
        var duplicateB = ProjectId.CreateNewId();
        var original = fixture.AddContext(fixture.Solution, id)
            .AddProject(ProjectInfo.Create(duplicateB, VersionStamp.Create(), "B-context", "B", LanguageNames.CSharp,
                filePath: fixture.Solution.GetProject(fixture.B)!.FilePath));
        var modified = original.WithProjectReferences(id,
            [new ProjectReference(fixture.B, ["alias"]), new ProjectReference(duplicateB, ["alias"])]);
        var mutations = await ProjectReferenceMutationRenderer.PlanAsync(original, modified, CancellationToken.None);
        var mutation = mutations.Single();
        Assert.HasCount(1, XDocument.Parse(CsprojSemanticEquality.CreateSnapshot(mutation.Modified).Content).Descendants("ProjectReference").ToArray());
        var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, CancellationToken.None);
        Assert.HasCount(1, changes.Where(change => change.FilePath == fixture.Path).ToArray());
        var store = new PreviewStore();
        var token = store.Store("physical-contexts", original, modified, 1, "physical addition", false);
        CollectionAssert.AreEqual(new[] { fixture.Path }, store.PeekChangedPaths(token)!.ToArray());
    }

    private sealed class PhysicalReferenceFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "physical-reference-preview", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public ProjectId B { get; } = ProjectId.CreateNewId();
        public ProjectId C { get; } = ProjectId.CreateNewId();
        public Solution Solution { get; }

        public PhysicalReferenceFixture(string xml)
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "A.csproj");
            File.WriteAllText(Path, xml);
            Solution = _workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(B, VersionStamp.Create(), "B", "B", LanguageNames.CSharp, filePath: System.IO.Path.Combine(_directory, "B.csproj")))
                .AddProject(ProjectInfo.Create(C, VersionStamp.Create(), "C", "C", LanguageNames.CSharp, filePath: System.IO.Path.Combine(_directory, "C.csproj")));
        }

        public Solution AddContext(Solution solution, ProjectId id, params ProjectReference[] references) => solution
            .AddProject(ProjectInfo.Create(id, VersionStamp.Create(), id.ToString(), "A", LanguageNames.CSharp, filePath: Path))
            .WithProjectReferences(id, references);

        public void Dispose()
        {
            _workspace.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

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
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddedCompilationReference_PreservesExistingBuildOnlyReference(bool useAttribute)
    {
        var root = Path.Combine(Path.GetTempPath(), "build-only-preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "A.csproj");
            await File.WriteAllTextAsync(path,
                useAttribute
                    ? "<Project><ItemGroup><ProjectReference Include=\"B.csproj\" ReferenceOutputAssembly=\"false\" /></ItemGroup></Project>"
                    : "<Project><ItemGroup><ProjectReference Include=\"B.csproj\"><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference></ItemGroup></Project>");
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
            Assert.HasCount(1, rendered.Descendants("ProjectReference").Where(element => (string?)element.Attribute("ReferenceOutputAssembly") == "false" || element.Element("ReferenceOutputAssembly")?.Value == "false").ToArray());
            Assert.HasCount(1, rendered.Descendants("ProjectReference").Where(element => element.Attribute("ReferenceOutputAssembly") is null && element.Element("ReferenceOutputAssembly") is null).ToArray());
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
