using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkReviewedGraphTests : OwnedRefactoringTestBase
{
    private readonly List<string> _packageRoots = [];

    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void CleanupPackageRoots()
    {
        foreach (var root in _packageRoots) TestFixtureFileSystem.DeleteDirectoryIfExists(root);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicMove_ForkReplaysReviewedGraph_AndPreservesSourceRedemption(bool siblingApply)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var scalar = Path.Combine(workspace.RootPath, "Source", "Scalar.cs");
        var consumer = Path.Combine(workspace.RootPath, "Source", "Consumer.cs");
        var sourceProject = Path.Combine(workspace.RootPath, "Source", "Source.csproj");
        var targetProject = Path.Combine(workspace.RootPath, "Shared", "Shared.csproj");
        var preview = await Services.CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
            workspace.WorkspaceId, scalar, "Scalar", "Shared", null, timeout.Token, preserveNamespace: true);
        Assert.IsTrue(preview.Changes.Any(change => FileSystemPath.Comparer.Equals(change.FilePath, sourceProject)
            && change.UnifiedDiff.Contains("ProjectReference", StringComparison.Ordinal)), "Public producer must expose its physical reference change.");
        Assert.IsTrue(preview.Changes.Any(change => FileSystemPath.Comparer.Equals(change.FilePath, scalar)));
        Assert.IsTrue(preview.Changes.Any(change => FileSystemPath.Comparer.Equals(change.FilePath,
            Path.Combine(workspace.RootPath, "Shared", "Scalar.cs"))));
        TestContext.WriteLine("PUBLIC_PREVIEW=" + JsonSerializer.Serialize(preview));

        if (siblingApply)
        {
            var before = Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
            var sibling = await Services.RefactoringService.PreviewFormatDocumentAsync(workspace.WorkspaceId, consumer, timeout.Token);
            Assert.IsTrue(sibling.Changes.Count > 0, "Sibling must change real unformatted source.");
            var apply = await Services.RefactoringService.ApplyRefactoringAsync(sibling.PreviewToken, "format_document_apply", timeout.Token);
            Assert.IsTrue(apply.Success, apply.Error);
            Assert.IsTrue(Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId) > before,
                "The real sibling apply must advance source version.");
            Assert.IsNotNull(Services.PreviewStore.Retrieve(preview.PreviewToken), "Store retains the earlier public token.");
        }

        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var version = Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var snapshot = (await Services.WorkspaceManager.GetStatusAsync(workspace.WorkspaceId)).SnapshotToken;
        var entry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(Services.PreviewStore, preview.PreviewToken);
        var undo = Services.UndoService.GetLastOperation(workspace.WorkspaceId);
        var sourceBytes = Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var pair = Services.PreviewStore.Retrieve(preview.PreviewToken)!.Value;
        var physicalPlan = await ProjectReferenceMutationRenderer.PlanAsync(pair.OriginalSolution, pair.ModifiedSolution, timeout.Token);
        var expectedProject = physicalPlan.Single(mutation => FileSystemPath.Comparer.Equals(mutation.Path, sourceProject)).Modified;
        var runner = new ReviewedGraphRunner(Services.DotnetCommandRunner, expectedProject, TestContext);
        await using var service = CreateForkService(runner);
        WorkspaceForkApplyResultDto? result = null;
        try
        {
            var json = await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId,
                preview.PreviewToken, "keep", false, null, "reviewed-move", timeout.Token);
            result = JsonSerializer.Deserialize<WorkspaceForkApplyResultDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success, json);
            Assert.AreEqual(0, result.CleanupWarnings.Count, json);
            Assert.IsTrue(runner.Calls > 0, "The real fork restore boundary must be exercised.");
            var forkConsumer = Path.Combine(result.ForkPath, "Source", "Consumer.cs");
            CollectionAssert.AreEqual(sourceBytes[consumer], File.ReadAllBytes(forkConsumer), "Current sibling bytes must survive fork replay.");
            AssertSourceAuthority();

            var apply = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "move_type_to_project_apply", timeout.Token);
            Assert.IsTrue(apply.Success, apply.Error);
            AssertMatchesRenderedProjectWithOwnedImport(File.ReadAllBytes(sourceProject),
                Path.Combine(result.ForkPath, "Source", "Source.csproj"));
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(result.ForkPath, "Shared", "Scalar.cs")), File.ReadAllBytes(Path.Combine(workspace.RootPath, "Shared", "Scalar.cs")));
            Assert.IsFalse(File.Exists(scalar));
            Assert.IsNull(Services.PreviewStore.Retrieve(preview.PreviewToken));
            Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
            CollectionAssert.AreEqual(sourceBytes[sourceProject], File.ReadAllBytes(sourceProject));
            CollectionAssert.AreEqual(sourceBytes[scalar], File.ReadAllBytes(scalar));
            CollectionAssert.AreEqual(sourceBytes[consumer], File.ReadAllBytes(consumer));
        }
        catch
        {
            if (result is null) AssertSourceAuthority();
            throw;
        }
        finally
        {
            if (result?.ForkWorkspaceId is { } id) Services.WorkspaceManager.Close(id);
        }

        void AssertSourceAuthority()
        {
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.AreEqual(version, Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
            Assert.AreEqual(snapshot, Services.WorkspaceManager.GetStatus(workspace.WorkspaceId).SnapshotToken);
            Assert.AreEqual(entry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(Services.PreviewStore, preview.PreviewToken));
            Assert.AreEqual(undo, Services.UndoService.GetLastOperation(workspace.WorkspaceId));
            foreach (var (path, bytes) in sourceBytes) CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), path);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicFormat_ForkRestoreRetainsRecordedRootsAfterObjExclusion(bool mixed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token, mixed);
        var consumer = Path.Combine(workspace.RootPath, "Source", "Consumer.cs");
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "Source", "Shared" })
        {
            var project = Path.Combine(workspace.RootPath, name, name + ".csproj");
            roots.Add(name, RestoreStalenessDetector.TryReadRestorePackagesPath(project, MsBuildMetadataHelper.DefaultGlobalProperties)
                ?? throw new AssertFailedException("Real source assets must record a package root."));
        }
        var preview = await Services.RefactoringService.PreviewFormatDocumentAsync(workspace.WorkspaceId, consumer, timeout.Token);
        Assert.IsTrue(preview.Changes.Count > 0);
        var runner = new RecordedRootsRunner(Services.DotnetCommandRunner, roots, mixed, TestContext);
        await using var service = CreateForkService(runner);
        var json = await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId,
            preview.PreviewToken, "drop-always", false, null, "recorded-roots", timeout.Token);
        using var result = JsonDocument.Parse(json);
        Assert.IsTrue(result.RootElement.GetProperty("success").GetBoolean(), json);
        Assert.AreEqual(mixed ? 2 : 1, runner.Calls);
        Assert.IsNotNull(Services.PreviewStore.Retrieve(preview.PreviewToken));
    }

    private WorkspaceForkApplyService CreateForkService(IDotnetCommandRunner runner)
    {
        var validation = new WorkspaceValidationService(Services.CompileCheckService, Services.DiagnosticService,
            Services.TestDiscoveryService, Services.TestRunnerService, Services.WorkspaceManager, Services.ChangeTracker);
        return new WorkspaceForkApplyService(Services.WorkspaceManager, Services.WorkspaceExecutionGate,
            Services.PreviewStore, validation, Services.TestRunnerService, runner,
            Helpers.ForkStorageFixtureBoundary.ValidateAsync, NullLogger<WorkspaceForkApplyService>.Instance,
            Services.WorkspaceManager.RunWithRetainedForkSourceAsync, Services.WorkspaceManager.TryReserveForkStorageDeletion);
    }

    [TestMethod]
    public async Task PublicFormat_RelocatedConditionalReferenceDivergence_IsRefusedBeforeRunner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var project = Path.Combine(workspace.RootPath, "Source", "Source.csproj");
        var xml = XDocument.Load(project, LoadOptions.PreserveWhitespace);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", "../Shared/Shared.csproj"),
            new XAttribute("Condition", "'$(MSBuildProjectDirectory)' == '" + Path.GetDirectoryName(project) + "'"))));
        xml.Save(project, SaveOptions.DisableFormatting);
        var restore = await Services.DotnetCommandRunner.RunAsync(workspace.RootPath, workspace.SolutionPath,
            ["restore", workspace.SolutionPath, "--nologo"], timeout.Token);
        Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
        await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, timeout.Token);
        var source = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.Single(item => item.Name == "Source");
        Assert.AreEqual(1, source.ProjectReferences.Count(), "Source graph genuinely has the conditionally selected reference.");
        var preview = await Services.RefactoringService.PreviewFormatDocumentAsync(workspace.WorkspaceId,
            Path.Combine(workspace.RootPath, "Source", "Consumer.cs"), timeout.Token);
        var runner = new UnexpectedEffectRunner(Services.DotnetCommandRunner, TestContext);
        await using var service = CreateForkService(runner);
        Exception? refusal = null;
        try
        {
            await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, preview.PreviewToken,
                "drop-always", false, null, "fidelity-refusal", timeout.Token);
        }
        catch (Exception ex) when (ex is IPublicMessageException) { refusal = ex; }
        Assert.AreEqual(0, runner.Calls, "Copied evaluated graph lost a real source reference after relocation, but the runner still executed before fidelity verification.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        Assert.IsNotNull(Services.PreviewStore.Retrieve(preview.PreviewToken));
    }

    [TestMethod]
    public async Task AdditionalDocumentSnapshot_IsRefusedBeforeCopyOrRunner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var source = original.Projects.Single(project => project.Name == "Source");
        var modified = original.AddAdditionalDocument(DocumentId.CreateNewId(source.Id), "reviewed.txt",
            SourceText.From("reviewed additional input"), filePath: Path.Combine(workspace.RootPath, "Source", "reviewed.txt"));
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "additional-document snapshot", false);
        var runner = new UnexpectedEffectRunner(Services.DotnetCommandRunner, TestContext);
        await using var service = CreateForkService(runner);
        Exception? refusal = null;
        try
        {
            await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
                "drop-always", false, null, "unsupported-snapshot", timeout.Token);
        }
        catch (Exception ex) when (ex is IPublicMessageException) { refusal = ex; }
        Assert.AreEqual(0, runner.Calls, "An unsupported additional-document snapshot was silently discarded and reached restore.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.RootPath, ".roslynmcp", "forks")), "Refusal must precede pool creation and copying.");
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
    }

    private sealed class UnexpectedEffectRunner(IDotnetCommandRunner inner, TestContext context) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            using var collection = new ProjectCollection();
            var project = new ProjectInstance(Path.Combine(workingDirectory, "Source", "Source.csproj"), null, null, collection);
            context.WriteLine("UNEXPECTED_RUNNER_EVALUATED_REFERENCES=" + JsonSerializer.Serialize(project.GetItems("ProjectReference").Select(item => item.EvaluatedInclude)));
            return inner.RunAsync(workingDirectory, targetPath, arguments, ct);
        }
    }

    [TestMethod]
    [DataRow("Exclude", false)]
    [DataRow("Remove", false)]
    [DataRow("Remove", true)]
    public async Task AddedDocumentExcludedByNativeCompileInventory_IsRefusedBeforeCopy(string operation, bool multipleFrameworks)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var projectPath = Path.Combine(workspace.RootPath, "Source", "Source.csproj");
        var xml = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var root = xml.Root!;
        if (multipleFrameworks)
        {
            var framework = root.Descendants("TargetFramework").Single();
            framework.Name = "TargetFrameworks";
            framework.Value = "net10.0;net8.0";
        }
        if (operation == "Exclude")
            root.Element("PropertyGroup")!.Add(new XElement("DefaultItemExcludes", "$(DefaultItemExcludes);Rejected.cs"));
        else root.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Remove", "Rejected.cs"))));
        xml.Save(projectPath, SaveOptions.DisableFormatting);
        var restore = await Services.DotnetCommandRunner.RunAsync(workspace.RootPath, workspace.SolutionPath,
            ["restore", workspace.SolutionPath, "--nologo"], timeout.Token);
        Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
        await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sourceContexts = original.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, projectPath)).ToArray();
        Assert.AreEqual(multipleFrameworks ? 2 : 1, sourceContexts.Length, "The actual loaded compilation contexts must be present.");
        var modified = original;
        foreach (var project in sourceContexts)
            modified = modified.AddDocument(DocumentId.CreateNewId(project.Id), "Rejected.cs",
                SourceText.From("namespace Domain; public class Rejected { }"), filePath: Path.Combine(workspace.RootPath, "Source", "Rejected.cs"));
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "native Compile exclusion snapshot", false);
        var runner = new UnexpectedEffectRunner(Services.DotnetCommandRunner, TestContext);
        await using var service = CreateForkService(runner);
        Exception? refusal = null;
        try
        {
            await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
                "drop-always", false, null, "excluded-compile", timeout.Token);
        }
        catch (Exception ex) when (ex is IPublicMessageException) { refusal = ex; }
        Assert.AreEqual(0, runner.Calls, "A reviewed added document excluded by the actual native Compile inventory reached the runner.");
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.RootPath, ".roslynmcp", "forks")));
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
    }


    [TestMethod]
    [DataRow("text")]
    [DataRow("encoding")]
    [DataRow("remove")]
    [DataRow("agree")]
    public async Task LinkedDocument_ContradictoryFinalOwner_IsRefusedBeforeCopy(string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var sharedPath = Path.Combine(workspace.RootPath, "Linked.cs");
        File.WriteAllText(sharedPath, "namespace Domain; public class Linked { }\n", new System.Text.UTF8Encoding(false));
        foreach (var name in new[] { "Source", "Shared" })
        {
            var path = Path.Combine(workspace.RootPath, name, name + ".csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "../Linked.cs"))));
            xml.Save(path);
        }
        await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var owners = original.Projects.SelectMany(project => project.Documents)
            .Where(document => FileSystemPath.Comparer.Equals(document.FilePath, sharedPath)).ToArray();
        Assert.AreEqual(2, owners.Length);
        var modified = operation == "remove" ? original.RemoveDocument(owners[0].Id)
            : original.WithDocumentText(owners[0].Id, SourceText.From(
                operation == "text" ? "namespace Domain; public class ChangedLinked { }\n" : (await owners[0].GetTextAsync(timeout.Token)).ToString(),
                operation == "encoding" ? System.Text.Encoding.Unicode : new System.Text.UTF8Encoding(false)));
        if (operation == "agree")
        {
            foreach (var owner in owners)
                modified = modified.WithDocumentText(owner.Id, SourceText.From("namespace Domain; public class AgreedLinked { }\n", System.Text.Encoding.Unicode));
        }
        var token = ((PreviewStore)((Helpers.TrackingPreviewStore)Services.PreviewStore).Inner).Store(workspace.WorkspaceId, original, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "linked physical owners", false);
        if (operation != "agree") await AssertPreCopyRefusalAsync(workspace, token, original, timeout.Token);
        else
        {
            await using var service = CreateForkService(Services.DotnetCommandRunner);
            var json = await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
                "keep", false, null, "agreeing-linked", timeout.Token);
            var result = JsonSerializer.Deserialize<WorkspaceForkApplyResultDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            try
            {
                Assert.IsTrue(result.Success, json);
                var path = Path.Combine(result.ForkPath, "Linked.cs");
                var expected = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("namespace Domain; public class AgreedLinked { }\n")).ToArray();
                CollectionAssert.AreEqual(expected, File.ReadAllBytes(path));
                var actual = Services.WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
                Assert.AreEqual(2, actual.Projects.SelectMany(project => project.Documents).Count(document => FileSystemPath.Comparer.Equals(document.FilePath, path)));
                Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
                Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
            }
            finally { if (result.ForkWorkspaceId is { } id) Services.WorkspaceManager.Close(id); }
        }
    }

    [TestMethod]
    public async Task MissingReviewedDocumentBinding_IsRefusedBeforeCopy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var current = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var owner = current.Projects.Single(project => project.Name == "Source");
        var id = DocumentId.CreateNewId(owner.Id);
        var path = Path.Combine(workspace.RootPath, "Source", "RemovedBySibling.cs");
        var original = current.AddDocument(id, "RemovedBySibling.cs", SourceText.From("class Before { }"), filePath: path);
        var modified = original.WithDocumentText(id, SourceText.From("class ReviewedAfter { }"));
        var token = ((PreviewStore)((Helpers.TrackingPreviewStore)Services.PreviewStore).Inner).Store(workspace.WorkspaceId, original, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "missing reviewed target binding", false);
        await AssertPreCopyRefusalAsync(workspace, token, current, timeout.Token);
    }

    [TestMethod]
    public async Task ExpiredRequestDeadline_IsRefusedBeforeCopy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var preview = await Services.RefactoringService.PreviewFormatDocumentAsync(workspace.WorkspaceId,
            Path.Combine(workspace.RootPath, "Source", "Consumer.cs"), timeout.Token);
        var runner = new UnexpectedEffectRunner(Services.DotnetCommandRunner, TestContext);
        await using var service = CreateForkService(runner);
        using var deadline = RequestDeadline.Begin(TimeProvider.System, TimeSpan.Zero);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => ValidationBundleTools.WorkspaceForkApply(service,
            workspace.WorkspaceId, preview.PreviewToken, "drop-always", false, null, "expired-before-copy", timeout.Token));
        Assert.AreEqual(0, runner.Calls);
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.RootPath, ".roslynmcp", "forks")),
            "An already expired request must not create the pool before noticing its deadline.");
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.IsNotNull(Services.PreviewStore.Retrieve(preview.PreviewToken));
    }

    private async Task AssertPreCopyRefusalAsync(IsolatedWorkspaceTestBase.IsolatedWorkspaceScope workspace,
        string token, Solution original, CancellationToken ct)
    {
        var version = Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId);
        var snapshot = Services.WorkspaceManager.GetStatus(workspace.WorkspaceId).SnapshotToken;
        var entry = WorkspaceForkGraphReplayTests.CapturePreviewEntry(Services.PreviewStore, token);
        var undo = Services.UndoService.GetLastOperation(workspace.WorkspaceId);
        var bytes = Directory.GetFiles(workspace.RootPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var runner = new UnexpectedEffectRunner(Services.DotnetCommandRunner, TestContext);
        await using var service = CreateForkService(runner);
        Exception? refusal = null;
        try { await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
            "drop-always", false, null, "binding-owner-refusal", ct); }
        catch (Exception ex) when (ex is IPublicMessageException) { refusal = ex; }
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.RootPath, ".roslynmcp", "forks")),
            "Contradictory or missing reviewed intent must refuse before pool creation/copy.");
        Assert.AreEqual(0, runner.Calls);
        Assert.IsInstanceOfType<IPublicMessageException>(refusal);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(version, Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId));
        Assert.AreEqual(snapshot, Services.WorkspaceManager.GetStatus(workspace.WorkspaceId).SnapshotToken);
        Assert.AreEqual(entry, WorkspaceForkGraphReplayTests.CapturePreviewEntry(Services.PreviewStore, token));
        Assert.AreEqual(undo, Services.UndoService.GetLastOperation(workspace.WorkspaceId));
        foreach (var pair in bytes) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(pair.Key), pair.Key);
    }


    [TestMethod]
    [DataRow("default")]
    [DataRow("explicit")]
    [DataRow("unrelated-update")]
    [DataRow("inactive-update")]
    [DataRow("matching-metadata")]
    [DataRow("dynamic-metadata")]
    [DataRow("nested")]
    public async Task NativeCompileProjection_PreservesOrdinaryControls(string mode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var path = Path.Combine(workspace.RootPath, "Shared", "Shared.csproj");
        var xml = XDocument.Load(path);
        if (mode == "explicit")
        {
            xml.Root!.Element("PropertyGroup")!.Add(new XElement("EnableDefaultCompileItems", "false"));
        }
        if (mode.Contains("update", StringComparison.Ordinal) || mode == "matching-metadata")
        {
            var update = new XElement("Compile", new XAttribute("Update", mode == "matching-metadata" ? "AddedControl.cs" : "Unrelated.cs"),
                new XElement("Link", "Special.cs"));
            if (mode == "inactive-update") update.Add(new XAttribute("Condition", "'$(TargetFramework)' == 'never'"));
            xml.Root!.Add(new XElement("ItemGroup", update));
        }
        if (mode == "dynamic-metadata")
            xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Update", "@(Compile)"),
                new XElement("Probe", new XAttribute("Condition", "'%(Filename)' == 'AddedControl'"), "native-item-value"))));
        xml.Save(path);
        await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var owner = original.Projects.Single(project => project.Name == "Shared");
        var addedPath = mode == "nested" ? Path.Combine(workspace.RootPath, "Shared", "Nested", "AddedControl.cs")
            : Path.Combine(workspace.RootPath, "Shared", "AddedControl.cs");
        var modified = original.AddDocument(DocumentId.CreateNewId(owner.Id), "AddedControl.cs",
            SourceText.From("namespace Domain; public class AddedControl { }", System.Text.Encoding.Unicode), filePath: addedPath);
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "native compile control", false);
        await using var service = CreateForkService(Services.DotnetCommandRunner);
        var json = await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
            "keep", false, null, "native-compile-control", timeout.Token);
        var result = JsonSerializer.Deserialize<WorkspaceForkApplyResultDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        try
        {
            Assert.IsTrue(result.Success, json);
            using var collection = new ProjectCollection();
            var actual = new ProjectInstance(Path.Combine(result.ForkPath, "Shared", "Shared.csproj"), null, null, collection);
            var item = actual.GetItems("Compile").Single(item => Path.GetFileName(item.EvaluatedInclude) == "AddedControl.cs");
            if (mode == "matching-metadata") Assert.AreEqual("Special.cs", item.GetMetadataValue("Link"));
            if (mode == "dynamic-metadata") Assert.AreEqual("native-item-value", item.GetMetadataValue("Probe"));
            var forkAddedPath = Path.Combine(result.ForkPath, Path.GetRelativePath(workspace.RootPath, addedPath));
            CollectionAssert.AreEqual(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("namespace Domain; public class AddedControl { }")).ToArray(), File.ReadAllBytes(forkAddedPath));
            Assert.IsFalse(File.Exists(addedPath));
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
        }
        finally { if (result.ForkWorkspaceId is { } id) Services.WorkspaceManager.Close(id); }
    }

    [TestMethod]
    public async Task RemovedDocumentStillIncludedByImportedLiteral_IsRefusedBeforeCopy()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var projectPath = Path.Combine(workspace.RootPath, "Source", "Source.csproj");
        var imported = Path.Combine(workspace.RootPath, "Source", "literal.targets");
        File.WriteAllText(imported, "<Project><ItemGroup><Compile Include=\"Scalar.cs\" /></ItemGroup></Project>");
        var xml = XDocument.Load(projectPath);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Remove", "Scalar.cs"))));
        xml.Root.Add(new XElement("Import", new XAttribute("Project", "literal.targets")));
        xml.Save(projectPath);
        await Services.WorkspaceManager.ReloadAsync(workspace.WorkspaceId, timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var document = original.Projects.Single(project => project.Name == "Source").Documents.Single(document => document.Name == "Scalar.cs");
        var modified = original.RemoveDocument(document.Id);
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "imported literal removal", false);
        await AssertPreCopyRefusalAsync(workspace, token, original, timeout.Token);
    }

    [TestMethod]
    [DataRow("move", false)]
    [DataRow("move", true)]
    [DataRow("utf16-add", false)]
    [DataRow("utf16-add", true)]
    [DataRow("utf16-change", false)]
    [DataRow("utf16-change", true)]
    [DataRow("default-add", false)]
    [DataRow("default-add", true)]
    [DataRow("default-change", false)]
    [DataRow("default-change", true)]
    [DataRow("samepath", false)]
    [DataRow("samepath", true)]
    public async Task SourceApply_PreservesIntendedDocumentEncoding(string operation, bool siblingApply)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var sourcePath = Path.Combine(workspace.RootPath, "Source", "Scalar.cs");
        var originalSourceBytes = File.ReadAllBytes(sourcePath);
        string token;
        string targetPath;
        string applyTool;
        if (operation == "move")
        {
            var preview = await Services.CrossProjectRefactoringService.PreviewMoveTypeToProjectAsync(
                workspace.WorkspaceId, sourcePath, "Scalar", "Shared", null, timeout.Token, preserveNamespace: true);
            token = preview.PreviewToken;
            targetPath = Path.Combine(workspace.RootPath, "Shared", "Scalar.cs");
            applyTool = "move_type_to_project_apply";
        }
        else
        {
            var source = original.Projects.Single(project => project.Name == "Source");
            var document = source.Documents.Single(document => document.Name == "Scalar.cs");
            var selectedEncoding = operation.StartsWith("utf16", StringComparison.Ordinal) || operation == "samepath" ? System.Text.Encoding.Unicode : null;
            var text = SourceText.From("namespace Domain; public class Scalar { public int Value => 43; public string Text => \"caf\u00e9\"; }\n", selectedEncoding, SourceHashAlgorithm.Sha256);
            var added = operation.EndsWith("add", StringComparison.Ordinal);
            targetPath = added ? Path.Combine(workspace.RootPath, "Shared", "Encoded.cs") : sourcePath;
            var modified = added
                ? original.AddDocument(DocumentId.CreateNewId(original.Projects.Single(project => project.Name == "Shared").Id),
                    "Encoded.cs", SourceText.From("namespace Domain; public class Encoded { public string Value => \"caf\u00e9\"; }\n", selectedEncoding, SourceHashAlgorithm.Sha256), filePath: targetPath)
                : operation == "samepath"
                    ? original.RemoveDocument(document.Id).AddDocument(DocumentId.CreateNewId(source.Id), document.Name, text, filePath: sourcePath)
                    : original.WithDocumentText(document.Id, text);
            token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
                Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "source encoding proof", false);
            applyTool = "test_apply";
        }
        var entry = Services.PreviewStore.Retrieve(token)!.Value;
        var intended = entry.ModifiedSolution.Projects.SelectMany(project => project.Documents)
            .Single(document => FileSystemPath.Comparer.Equals(document.FilePath, targetPath));
        var intendedText = await intended.GetTextAsync(timeout.Token);
        if (siblingApply)
        {
            var sibling = await Services.RefactoringService.PreviewFormatDocumentAsync(workspace.WorkspaceId,
                Path.Combine(workspace.RootPath, "Source", "Consumer.cs"), timeout.Token);
            Assert.IsTrue(sibling.Changes.Count > 0);
            var siblingResult = await Services.RefactoringService.ApplyRefactoringAsync(sibling.PreviewToken, "format_document_apply", timeout.Token);
            Assert.IsTrue(siblingResult.Success, siblingResult.Error);
            Assert.IsNotNull(Services.PreviewStore.Retrieve(token));
        }
        var current = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var projected = await RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(entry.OriginalSolution, entry.ModifiedSolution, current, timeout.Token);
        var projectedText = await projected.Projects.SelectMany(project => project.Documents)
            .Single(document => FileSystemPath.Comparer.Equals(document.FilePath, targetPath)).GetTextAsync(timeout.Token);
        var consumerPath = Path.Combine(workspace.RootPath, "Source", "Consumer.cs");
        var consumerBytes = File.ReadAllBytes(consumerPath);
        var encoding = SourceFileEncoding.FromSourceText(intendedText.Encoding);
        var expectedBytes = encoding.GetPreamble().Concat(encoding.GetBytes(intendedText.ToString())).ToArray();
        TestContext.WriteLine("ENCODING_PROVENANCE=" + JsonSerializer.Serialize(new
        {
            operation,
            siblingApply,
            sourcePreamble = Convert.ToHexString(SourceFileEncoding.FromBytes(File.ReadAllBytes(sourcePath)).GetPreamble()),
            intendedCodePage = intendedText.Encoding?.CodePage,
            intendedPreamble = Convert.ToHexString(intendedText.Encoding?.GetPreamble() ?? []),
            sharedHelperPreamble = Convert.ToHexString(encoding.GetPreamble()),
        }));
        var result = await Services.RefactoringService.ApplyRefactoringAsync(token, applyTool, timeout.Token);
        Assert.IsTrue(result.Success, result.Error);
        var actualBytes = File.ReadAllBytes(targetPath);
        TestContext.WriteLine("SOURCE_APPLY_PREAMBLE=" + Convert.ToHexString(SourceFileEncoding.FromBytes(actualBytes).GetPreamble()));
        CollectionAssert.AreEqual(expectedBytes, actualBytes, "Source redemption must preserve intended SourceText encoding through the shared helper.");
        Assert.IsNotNull(projectedText.Encoding);
        Assert.AreEqual(encoding.CodePage, projectedText.Encoding.CodePage);
        CollectionAssert.AreEqual(encoding.GetPreamble(), projectedText.Encoding.GetPreamble());
        Assert.AreEqual(intendedText.ChecksumAlgorithm, projectedText.ChecksumAlgorithm);
        var appliedDocument = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId).Projects.SelectMany(project => project.Documents)
            .Single(document => FileSystemPath.Comparer.Equals(document.FilePath, targetPath));
        var appliedText = await appliedDocument.GetTextAsync(timeout.Token);
        Assert.IsNotNull(appliedText.Encoding, "Native workspace must retain the resolved physical encoding.");
        Assert.AreEqual(encoding.CodePage, appliedText.Encoding.CodePage);
        CollectionAssert.AreEqual(encoding.GetPreamble(), appliedText.Encoding.GetPreamble());
        Assert.AreEqual(intendedText.ChecksumAlgorithm, appliedText.ChecksumAlgorithm);
        var storedText = await intended.GetTextAsync(timeout.Token);
        Assert.AreSame(intendedText.Encoding, storedText.Encoding, "Stored preview snapshots stay immutable.");
        Assert.AreEqual(intendedText.ChecksumAlgorithm, storedText.ChecksumAlgorithm);
        Assert.IsNull(Services.PreviewStore.Retrieve(token));
        CollectionAssert.AreEqual(consumerBytes, File.ReadAllBytes(consumerPath));
        Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
        CollectionAssert.AreEqual(originalSourceBytes, File.ReadAllBytes(sourcePath));
        CollectionAssert.AreEqual(consumerBytes, File.ReadAllBytes(consumerPath));
        if (!FileSystemPath.Comparer.Equals(sourcePath, targetPath)) Assert.IsFalse(File.Exists(targetPath));
    }

    [TestMethod]
    public async Task SamePathDocumentReplacement_PreservesFinalMembershipAndSourceRedemption()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var workspace = await CreateFixtureAsync(timeout.Token);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var owner = original.Projects.Single(project => project.Name == "Source");
        var before = owner.Documents.Single(document => document.Name == "Scalar.cs");
        var text = SourceText.From("namespace Domain; public class Scalar { public int Value => 44; }\n", System.Text.Encoding.Unicode);
        var modified = original.RemoveDocument(before.Id).AddDocument(DocumentId.CreateNewId(owner.Id), before.Name, text, filePath: before.FilePath);
        var token = Services.PreviewStore.Store(workspace.WorkspaceId, modified,
            Services.WorkspaceManager.GetCurrentVersion(workspace.WorkspaceId), "same physical path replacement", false);
        var sourceBytes = File.ReadAllBytes(before.FilePath!);
        var expectedBytes = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(text.ToString())).ToArray();
        await using var service = CreateForkService(Services.DotnetCommandRunner);
        var json = await ValidationBundleTools.WorkspaceForkApply(service, workspace.WorkspaceId, token,
            "keep", false, null, "samepath-replacement", timeout.Token);
        var result = JsonSerializer.Deserialize<WorkspaceForkApplyResultDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        try
        {
            Assert.IsTrue(result.Success, json);
            var path = Path.Combine(result.ForkPath, "Source", "Scalar.cs");
            CollectionAssert.AreEqual(expectedBytes, File.ReadAllBytes(path));
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(before.FilePath!));
            Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
            var applied = await Services.RefactoringService.ApplyRefactoringAsync(token, "test_apply", timeout.Token);
            Assert.IsTrue(applied.Success, applied.Error);
            CollectionAssert.AreEqual(expectedBytes, File.ReadAllBytes(before.FilePath!));
            Assert.IsTrue(await Services.UndoService.RevertAsync(workspace.WorkspaceId, timeout.Token));
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(before.FilePath!));
        }
        finally { if (result.ForkWorkspaceId is { } id) Services.WorkspaceManager.Close(id); }
    }

    private async Task<IsolatedWorkspaceTestBase.IsolatedWorkspaceScope> CreateFixtureAsync(CancellationToken ct, bool mixedRoots = false)
    {
        var root = Path.Combine(TestTempRoot.Current, "reviewed-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var solution = Path.Combine(root, "Reviewed.slnx");
        var packageRoot = root + "-packages";
        _packageRoots.Add(packageRoot);
        var workspace = new IsolatedWorkspaceTestBase.IsolatedWorkspaceScope(root, solution, Services.WorkspaceManager);
        return await IsolatedWorkspaceTestBase.InitializeWithCleanupAsync(workspace, async (scope, token) =>
        {
            File.WriteAllText(solution, "<Solution><Project Path=\"Source/Source.csproj\" /><Project Path=\"Shared/Shared.csproj\" /></Solution>");
            foreach (var name in new[] { "Source", "Shared" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                var project = Path.Combine(directory, name + ".csproj");
                File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>");
                var packages = Path.Combine(packageRoot, mixedRoots ? name : "uniform");
                var restore = await Services.DotnetCommandRunner.RunAsync(root, project,
                    ["restore", project, "--nologo", "--packages", packages, "--no-dependencies"], token);
                Assert.IsTrue(restore.Succeeded, restore.StdOut + restore.StdErr);
            }
            File.WriteAllText(Path.Combine(root, "Source", "Scalar.cs"), "namespace Domain; public class Scalar { public int Value => 42; }\n");
            File.WriteAllText(Path.Combine(root, "Source", "Consumer.cs"), "namespace Domain; public class Consumer{public int Read()=>new Scalar().Value;}\n");
            await scope.LoadAsync(token);
        }, ct);
    }

    private sealed class ReviewedGraphRunner(IDotnetCommandRunner inner, byte[] expectedProject, TestContext context) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            var sourceProject = Path.Combine(workingDirectory, "Source", "Source.csproj");
            var targetProject = Path.Combine(workingDirectory, "Shared", "Shared.csproj");
            using var collection = new ProjectCollection();
            var source = new ProjectInstance(sourceProject, null, null, collection);
            var target = new ProjectInstance(targetProject, null, null, collection);
            var references = source.GetItems("ProjectReference").Select(item =>
                Path.GetFullPath(item.EvaluatedInclude, Path.GetDirectoryName(sourceProject)!)).ToArray();
            context.WriteLine("ACTUAL_COPIED_REFERENCES=" + JsonSerializer.Serialize(references));
            Assert.IsTrue(references.Contains(targetProject, FileSystemPath.Comparer),
                "The actual copied evaluated graph is missing the public producer's reviewed Source -> Shared reference before restore.");
            AssertMatchesRenderedProjectWithOwnedImport(expectedProject, sourceProject);
            Assert.IsFalse(source.GetItems("Compile").Any(item => Path.GetFileName(item.EvaluatedInclude) == "Scalar.cs"));
            Assert.IsTrue(target.GetItems("Compile").Any(item => Path.GetFileName(item.EvaluatedInclude) == "Scalar.cs"));
            return inner.RunAsync(workingDirectory, targetPath, arguments, ct);
        }
    }

    private sealed class RecordedRootsRunner(IDotnetCommandRunner inner, IReadOnlyDictionary<string, string> roots,
        bool mixed, TestContext context) : IDotnetCommandRunner
    {
        internal int Calls { get; private set; }

        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath,
            IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls++;
            context.WriteLine("ACTUAL_RESTORE_ARGV=" + JsonSerializer.Serialize(arguments));
            if (Calls == 1)
                Assert.IsFalse(Directory.GetFiles(workingDirectory, "project.assets.json", SearchOption.AllDirectories).Any(), "Source obj is excluded before fork restore.");
            var packageIndex = arguments.ToList().IndexOf("--packages");
            Assert.IsTrue(packageIndex >= 0 && packageIndex + 1 < arguments.Count,
                "Fork restore lost the nullable package-root authority recorded in real source assets before obj exclusion.");
            var expected = mixed ? roots[Path.GetFileNameWithoutExtension(targetPath)] : roots["Source"];
            Assert.IsTrue(FileSystemPath.Comparer.Equals(expected, arguments[packageIndex + 1]));
            Assert.AreEqual(mixed, arguments.Contains("--no-dependencies"));
            return inner.RunAsync(workingDirectory, targetPath, arguments, ct);
        }
    }

    private static void AssertMatchesRenderedProjectWithOwnedImport(byte[] expectedProject, string actualProject)
    {
        var configuration = ForkAdmissionMaterial.Find(actualProject)
            ?? throw new AssertFailedException("Copied project must retain verified native admission ownership.");
        Assert.IsTrue(File.Exists(configuration.GuardTargetsPath), "The owned guard must remain installed.");
        var actual = XDocument.Load(actualProject, LoadOptions.PreserveWhitespace);
        var root = actual.Root ?? throw new AssertFailedException("Copied project has no root.");
        var import = root.Elements(root.Name.Namespace + "Import").Single(element =>
            (string?)element.Attribute("Label") == ForkProjectImports.Label &&
            FileSystemPath.Comparer.Equals((string?)element.Attribute("Project"), configuration.GuardTargetsPath));
        Assert.AreEqual(2, import.Attributes().Count(), "Only the exact owned import may be normalized.");
        Assert.IsFalse(import.Nodes().Any(), "The owned import must not carry additional content.");
        using var input = new MemoryStream(expectedProject, writable: false);
        var expected = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        Assert.IsNotNull(expected.Root);
        expected.Root.Add(new XElement(import));
        var scratch = Path.Combine(TestTempRoot.Current, "expected-reviewed-project-" + Guid.NewGuid().ToString("N") + ".csproj");
        try
        {
            // The native installer owns its serialization as well as the guard Import.
            // Serialize expected bytes only; the actual source and fork stay untouched.
            ForkProjectImports.Write(scratch, expected, expectedProject, static () => { });
            CollectionAssert.AreEqual(File.ReadAllBytes(scratch), File.ReadAllBytes(actualProject),
                "Copied project bytes must equal the reviewed renderer output plus only the verified owned admission import.");
        }
        finally
        {
            if (File.Exists(scratch)) File.Delete(scratch);
        }
    }
}
