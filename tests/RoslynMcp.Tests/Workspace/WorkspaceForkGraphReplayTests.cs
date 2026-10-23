using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkGraphReplayTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task CrossProjectProducer_ExposesReferenceInReviewedDiff()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await LoadProducerWorkspaceAsync(workspace);
        var preview = await ExtractAsync(workspace);
        var change = preview.Changes.SingleOrDefault(change => change.FilePath == workspace.GetPath("SampleLib", "SampleLib.csproj"));
        Assert.IsNotNull(change, "A real cross-project producer must publish its project-reference mutation for review.");
        StringAssert.Contains(change.UnifiedDiff, "ProjectReference");
        StringAssert.Contains(change.UnifiedDiff, "Contracts.csproj");
    }

    [TestMethod]
    public async Task CrossProjectProducer_PeekChangedPathsIncludesProjectFile()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await LoadProducerWorkspaceAsync(workspace);
        var preview = await ExtractAsync(workspace);
        CollectionAssert.Contains(PreviewStore.PeekChangedPaths(preview.PreviewToken)!.ToArray(),
            workspace.GetPath("SampleLib", "SampleLib.csproj"),
            "The write boundary must include the project file that the producer will mutate.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CrossProjectProducer_ForkReplaysGraphBeforeRestore_PreservesSourceAndSibling(bool invertDependencies)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await LoadProducerWorkspaceAsync(workspace);
        var preview = invertDependencies
            ? await CrossProjectRefactoringService.PreviewDependencyInversionAsync(workspace.WorkspaceId,
                workspace.GetPath("SampleLib", "ScalarService.cs"), "ScalarService", "IScalarService", "Contracts", CancellationToken.None)
            : await ExtractAsync(workspace);
        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var dog = current.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Dog.cs");
        var siblingText = (await dog.GetTextAsync()).ToString() + "\n// independent sibling edit\n";
        var siblingToken = PreviewStore.Store(workspace.WorkspaceId,
            current.WithDocumentText(dog.Id, SourceText.From(siblingText)),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "sibling");
        var sibling = await RefactoringService.ApplyRefactoringAsync(siblingToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(sibling.Success, sibling.Error);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var projectPath = workspace.GetPath("SampleLib", "SampleLib.csproj");
        var sourcePath = workspace.GetPath("SampleLib", "ScalarService.cs");
        var projectBytes = await File.ReadAllBytesAsync(projectPath);
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var runner = new InspectingRunner(directory =>
        {
            var reference = XDocument.Load(Path.Combine(directory, "SampleLib", "SampleLib.csproj"))
                .Descendants("ProjectReference").SingleOrDefault(element =>
                    ((string?)element.Attribute("Include"))?.Replace('\\', '/').EndsWith("Contracts/Contracts.csproj", StringComparison.Ordinal) == true);
            Assert.IsNotNull(reference, "The fork graph must contain the preview's project reference BEFORE any restore process runs.");
            Assert.AreEqual(siblingText, File.ReadAllText(Path.Combine(directory, "SampleLib", "Dog.cs")));
        });
        WorkspaceForkApplyResultDto? result = null;
        try
        {
            result = await CreateService(runner).ApplyAsync(workspace.WorkspaceId, preview.PreviewToken,
                "keep", false, null, "graph-replay", CancellationToken.None);
            Assert.IsTrue(result.Success, "Cross-project interface extraction must compile with its replayed reference. "
                + System.Text.Json.JsonSerializer.Serialize(result));
            var fork = WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
            Assert.IsTrue(fork.Projects.Single(project => project.Name == "SampleLib").ProjectReferences.Any(reference =>
                fork.GetProject(reference.ProjectId)?.Name == "Contracts"));
            CollectionAssert.Contains(result.AppliedFiles.ToArray(), Path.Combine(result.ForkPath, "SampleLib", "SampleLib.csproj"));
            CollectionAssert.AreEqual(projectBytes, await File.ReadAllBytesAsync(projectPath));
            CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(sourcePath));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(preview.PreviewToken), "Forking must preserve source redemption.");
            var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.AreEqual(siblingText, await File.ReadAllTextAsync(dog.FilePath!));
            Assert.IsTrue(XDocument.Load(projectPath).Descendants("ProjectReference").Any(element =>
                ((string?)element.Attribute("Include"))?.Contains("Contracts", StringComparison.Ordinal) == true));
        }
        finally
        {
            if (result?.ForkWorkspaceId is { } forkId)
                WorkspaceManager.Close(forkId);
            if (result is not null)
                DeleteDirectoryIfExists(result.ForkPath);
        }
    }

    [TestMethod]
    public async Task ForkReferenceMetadata_IsPresentBeforeRestore()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleLib");
        var target = original.Projects.Single(project => project.Name == "Contracts");
        var modified = original.AddProjectReference(source.Id,
            new ProjectReference(target.Id, ImmutableArray.Create("ContractsAlias"), embedInteropTypes: true));
        var token = PreviewStore.Store(workspace.WorkspaceId, modified,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "reference metadata", false);
        var runner = new InspectingRunner(directory =>
        {
            var reference = XDocument.Load(Path.Combine(directory, "SampleLib", "SampleLib.csproj"))
                .Descendants("ProjectReference").SingleOrDefault();
            Assert.IsNotNull(reference, "Reference metadata must be replayed before restore.");
            Assert.AreEqual("ContractsAlias", reference.Element("Aliases")?.Value);
            Assert.AreEqual("true", reference.Element("EmbedInteropTypes")?.Value, true);
        }, stopAfterInspection: true);
        await Assert.ThrowsExactlyAsync<InspectionCompleteException>(() => CreateService(runner).ApplyAsync(
            workspace.WorkspaceId, token, "drop-always", false, null, "metadata", CancellationToken.None));
        Assert.AreEqual(1, runner.InvocationCount);
        Assert.IsNotNull(PreviewStore.Retrieve(token));
    }

    [TestMethod]
    public async Task SourceApply_ReferenceMetadataReplacement_SurvivesInterveningSiblingApply()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleApp");
        var oldReference = source.ProjectReferences.Single();
        var replacement = new ProjectReference(oldReference.ProjectId, ImmutableArray.Create("ReplacementAlias"), embedInteropTypes: true);
        var modified = original.RemoveProjectReference(source.Id, oldReference).AddProjectReference(source.Id, replacement);
        var token = PreviewStore.Store(workspace.WorkspaceId, modified,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "replace reference metadata");
        var dog = original.Projects.SelectMany(project => project.Documents).Single(document => document.Name == "Dog.cs");
        var siblingText = (await dog.GetTextAsync()).ToString() + "\n// sibling before reference replacement\n";
        var siblingToken = PreviewStore.Store(workspace.WorkspaceId,
            original.WithDocumentText(dog.Id, SourceText.From(siblingText)),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "independent sibling");
        var sibling = await RefactoringService.ApplyRefactoringAsync(siblingToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(sibling.Success, sibling.Error);
        var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var references = current.Projects.Single(project => project.Name == "SampleApp").ProjectReferences.ToArray();
        Assert.HasCount(1, references, "Metadata replacement must retain the existing graph edge after sibling rebasing.");
        CollectionAssert.AreEqual(new[] { "ReplacementAlias" }, references[0].Aliases.ToArray());
        Assert.IsTrue(references[0].EmbedInteropTypes);
        var xmlReference = XDocument.Load(source.FilePath!).Descendants("ProjectReference").Single();
        Assert.AreEqual("ReplacementAlias", xmlReference.Element("Aliases")?.Value);
        Assert.AreEqual("true", xmlReference.Element("EmbedInteropTypes")?.Value, true);
        Assert.AreEqual(siblingText, await File.ReadAllTextAsync(dog.FilePath!));
    }

    [TestMethod]
    public async Task UnsupportedGraphDelta_IsRefusedBeforeCopyAndRestore()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleLib");
        var options = (CSharpCompilationOptions)source.CompilationOptions!;
        var modified = original.WithProjectCompilationOptions(source.Id, options.WithAllowUnsafe(!options.AllowUnsafe));
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var bytes = await File.ReadAllBytesAsync(source.FilePath!);
        var token = PreviewStore.Store(workspace.WorkspaceId, modified, version, "unsupported options", false);
        var runner = new InspectingRunner(_ => { }, stopAfterInspection: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CreateService(runner).ApplyAsync(
            workspace.WorkspaceId, token, "drop-always", false, null, "unsupported", CancellationToken.None));
        Assert.AreEqual(0, runner.InvocationCount, "Unsupported graph changes must be refused before invoking restore.");
        Assert.IsFalse(Directory.Exists(workspace.GetPath(".roslynmcp", "forks")), "Preflight must precede fork creation and copying.");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source.FilePath!));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.IsNotNull(PreviewStore.Retrieve(token));
    }

    [TestMethod]
    public async Task OlderReferencePreview_ForkPreservesAcceptedSiblingReference()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        AddProjectToCopiedSolution(workspace.RootPath, "SiblingContracts", "net10.0");
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleLib");
        var target = original.Projects.Single(project => project.Name == "Contracts");
        var siblingTarget = original.Projects.Single(project => project.Name == "SiblingContracts");
        var token = PreviewStore.Store(workspace.WorkspaceId,
            original.AddProjectReference(source.Id, new ProjectReference(target.Id)),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "older reference preview");
        var siblingToken = PreviewStore.Store(workspace.WorkspaceId,
            original.AddProjectReference(source.Id, new ProjectReference(siblingTarget.Id)),
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "sibling reference");
        var sibling = await RefactoringService.ApplyRefactoringAsync(siblingToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(sibling.Success, sibling.Error);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var bytes = await File.ReadAllBytesAsync(source.FilePath!);
        var runner = new InspectingRunner(directory => AssertReferenceTargets(
            Path.Combine(directory, "SampleLib", "SampleLib.csproj"), "Contracts.csproj", "SiblingContracts.csproj"), true);
        await Assert.ThrowsExactlyAsync<InspectionCompleteException>(() => CreateService(runner).ApplyAsync(
            workspace.WorkspaceId, token, "drop-always", false, null, "sibling-reference", CancellationToken.None));
        Assert.AreEqual(1, runner.InvocationCount);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(source.FilePath!));
        Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.IsNotNull(PreviewStore.Retrieve(token));
        var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        AssertReferenceTargets(source.FilePath!, "Contracts.csproj", "SiblingContracts.csproj");
        var current = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        CollectionAssert.AreEquivalent(new[] { "Contracts", "SiblingContracts" },
            current.Projects.Single(project => project.Name == "SampleLib").ProjectReferences
                .Select(reference => current.GetProject(reference.ProjectId)!.Name).ToArray());
    }

    [TestMethod]
    public async Task ExplicitCompilePreview_ReviewsCompileAndReferenceInOneProjectDiff()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var (original, modified, projectPath, _) = await PrepareExplicitCompilePreviewAsync(workspace);
        var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, CancellationToken.None);
        var projectChange = changes.Single(change => change.FilePath == projectPath);
        StringAssert.Contains(projectChange.UnifiedDiff, "Compile Include=");
        StringAssert.Contains(projectChange.UnifiedDiff, "Added.cs");
        StringAssert.Contains(projectChange.UnifiedDiff, "ProjectReference");
        StringAssert.Contains(projectChange.UnifiedDiff, "Contracts.csproj");
    }

    [TestMethod]
    public async Task ExplicitCompilePreview_ForkSourceAndUndoHavePhysicalGraphParity()
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var (_, modified, projectPath, addedPath) = await PrepareExplicitCompilePreviewAsync(workspace);
        var originalBytes = await File.ReadAllBytesAsync(projectPath);
        var version = WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var token = PreviewStore.Store(workspace.WorkspaceId, modified, version, "explicit compile and reference");
        var runner = new InspectingRunner(directory =>
        {
            var forkProjectPath = Path.Combine(directory, "ExplicitHost", "ExplicitHost.csproj");
            AssertReferenceTargets(forkProjectPath, "Contracts.csproj");
            Assert.IsTrue(XDocument.Load(forkProjectPath).Descendants("Compile").Any(element =>
                (string?)element.Attribute("Include") == "Added.cs"),
                "Explicit Compile ownership must exist before restore sees the fork project.");
            Assert.IsTrue(File.Exists(Path.Combine(directory, "ExplicitHost", "Added.cs")));
        });
        WorkspaceForkApplyResultDto? result = null;
        try
        {
            result = await CreateService(runner).ApplyAsync(workspace.WorkspaceId, token,
                "keep", false, null, "explicit-compile", CancellationToken.None);
            Assert.IsTrue(result.Success);
            var fork = WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
            var project = fork.Projects.Single(project => project.Name == "ExplicitHost");
            Assert.IsTrue(project.Documents.Any(document => document.Name == "Added.cs"));
            Assert.HasCount(1, project.ProjectReferences.ToArray());
            var forkBytes = await File.ReadAllBytesAsync(Path.Combine(result.ForkPath, "ExplicitHost", "ExplicitHost.csproj"));
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(projectPath));
            Assert.IsFalse(File.Exists(addedPath));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
            var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            CollectionAssert.AreEqual(forkBytes, await File.ReadAllBytesAsync(projectPath),
                "Preview replay and source apply must render the same physical project XML.");
            Assert.IsTrue(File.Exists(addedPath));
            Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId, CancellationToken.None));
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(projectPath));
            Assert.IsFalse(File.Exists(addedPath), "Undo must remove the added physical document.");
            var reverted = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.Single(project => project.Name == "ExplicitHost");
            Assert.IsFalse(reverted.Documents.Any(document => document.Name == "Added.cs"));
            Assert.HasCount(0, reverted.ProjectReferences.ToArray());
        }
        finally
        {
            if (result?.ForkWorkspaceId is { } forkId)
                WorkspaceManager.Close(forkId);
            if (result is not null)
                DeleteDirectoryIfExists(result.ForkPath);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReferenceMetadataReplacement_PreservesUnmodeledXmlAndUtf16Encoding(bool fork)
    {
        await using var workspace = CreateIsolatedWorkspaceCopy();
        var projectPath = workspace.GetPath("SampleApp", "SampleApp.csproj");
        var xml = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var reference = xml.Descendants("ProjectReference").Single();
        reference.SetAttributeValue("Condition", "'1' == '1'");
        reference.Add(new XElement("Private", "false"), new XElement("AdditionalProperties", "GraphMarker=preserved"));
        xml.Root!.Add(new XComment("unrelated project marker"), new XElement("PropertyGroup", new XElement("UnrelatedMarker", "preserved")));
        await File.WriteAllTextAsync(projectPath, xml.ToString(SaveOptions.DisableFormatting), Encoding.Unicode);
        var bytes = await File.ReadAllBytesAsync(projectPath);
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "SampleApp");
        var oldReference = source.ProjectReferences.Single();
        var modified = original.RemoveProjectReference(source.Id, oldReference).AddProjectReference(source.Id,
            new ProjectReference(oldReference.ProjectId, ImmutableArray.Create("global", "ReplacementAlias"), embedInteropTypes: false));
        var token = PreviewStore.Store(workspace.WorkspaceId, modified,
            WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "metadata with physical XML");
        if (fork)
        {
            var runner = new InspectingRunner(directory => AssertPreservedReferenceXml(
                Path.Combine(directory, "SampleApp", "SampleApp.csproj")), true);
            await Assert.ThrowsExactlyAsync<InspectionCompleteException>(() => CreateService(runner).ApplyAsync(
                workspace.WorkspaceId, token, "drop-always", false, null, "xml-metadata", CancellationToken.None));
            Assert.AreEqual(1, runner.InvocationCount);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(projectPath));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        else
        {
            var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            AssertPreservedReferenceXml(projectPath);
            Assert.IsTrue(await UndoService.RevertAsync(workspace.WorkspaceId, CancellationToken.None));
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(projectPath));
        }
    }

    private static void AssertPreservedReferenceXml(string path)
    {
        var bytes = File.ReadAllBytes(path);
        CollectionAssert.AreEqual(Encoding.Unicode.GetPreamble(), bytes.Take(2).ToArray(), "Project encoding must remain UTF-16 LE with BOM.");
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var reference = document.Descendants("ProjectReference").Single();
        Assert.AreEqual("'1' == '1'", (string?)reference.Attribute("Condition"), "Changing aliases must preserve the reference condition.");
        Assert.AreEqual("false", reference.Element("Private")?.Value);
        Assert.AreEqual("GraphMarker=preserved", reference.Element("AdditionalProperties")?.Value);
        Assert.AreEqual("global,ReplacementAlias", reference.Element("Aliases")?.Value);
        Assert.AreEqual("preserved", document.Descendants("UnrelatedMarker").Single().Value);
        Assert.IsTrue(document.DescendantNodes().OfType<XComment>().Any(comment => comment.Value == "unrelated project marker"));
    }

    private static void AssertReferenceTargets(string path, params string[] expected)
    {
        CollectionAssert.AreEquivalent(expected, XDocument.Load(path).Descendants("ProjectReference")
            .Select(element => Path.GetFileName(((string)element.Attribute("Include")!).Replace('\\', '/'))).ToArray());
    }

    private static async Task<(Solution Original, Solution Modified, string ProjectPath, string AddedPath)> PrepareExplicitCompilePreviewAsync(IsolatedWorkspaceScope workspace)
    {
        AddProjectToCopiedSolution(workspace.RootPath, "Contracts", "net10.0");
        AddProjectToCopiedSolution(workspace.RootPath, "ExplicitHost", "net10.0");
        var path = workspace.GetPath("ExplicitHost", "ExplicitHost.csproj");
        var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        xml.Root!.Element("PropertyGroup")!.Add(new XElement("EnableDefaultCompileItems", "false"));
        xml.Save(path, SaveOptions.DisableFormatting);
        await workspace.LoadAsync();
        var original = WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "ExplicitHost");
        var target = original.Projects.Single(project => project.Name == "Contracts");
        var addedPath = workspace.GetPath("ExplicitHost", "Added.cs");
        var modified = original.AddDocument(DocumentId.CreateNewId(source.Id), "Added.cs",
            SourceText.From("namespace ExplicitHost; public class Added { public int Value => 42; }\n"), filePath: addedPath)
            .AddProjectReference(source.Id, new ProjectReference(target.Id));
        return (original, modified, path, addedPath);
    }

    private static Task<RefactoringPreviewDto> ExtractAsync(IsolatedWorkspaceScope workspace) =>
        CrossProjectRefactoringService.PreviewExtractInterfaceAsync(workspace.WorkspaceId,
            workspace.GetPath("SampleLib", "ScalarService.cs"), "ScalarService", "IScalarService", "Contracts", CancellationToken.None);

    private static async Task LoadProducerWorkspaceAsync(IsolatedWorkspaceScope workspace)
    {
        await File.WriteAllTextAsync(workspace.GetPath("SampleLib", "ScalarService.cs"),
            "namespace SampleLib; public class ScalarService { public int Count(int value) => value; }\n");
        await workspace.LoadAsync();
    }

    private static WorkspaceForkApplyService CreateService(IDotnetCommandRunner runner) =>
        new(WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
            new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker),
            TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance);

    private sealed class InspectionCompleteException : Exception;

    private sealed class InspectingRunner(Action<string> inspect, bool stopAfterInspection = false) : IDotnetCommandRunner
    {
        public int InvocationCount { get; private set; }
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            InvocationCount++;
            var forkRoot = workingDirectory;
            while (!File.Exists(Path.Combine(forkRoot, "SampleSolution.slnx")))
                forkRoot = Path.GetDirectoryName(forkRoot) ?? throw new AssertFailedException("Restore invocation is outside the copied solution.");
            inspect(forkRoot);
            if (stopAfterInspection)
                throw new InspectionCompleteException();
            return new DotnetCommandRunner().RunAsync(workingDirectory, targetPath, arguments, ct);
        }
    }
}
