using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceForkGraphReplayTests : WorkspaceForkGraphReplayFixture
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task NativeMultiTargetGraph_UsesDistinctEvaluatedProjectContexts()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure27 = null;
        try
        {
            var projectPath = await PrepareNativeMultiTargetProjectAsync(source);
            var loaded = await WorkspaceManager.LoadAsync(projectPath, OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            Exception? closeFailure30 = null;
            try
            {
                var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
                var projects = solution.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, projectPath)).ToArray();
                Assert.AreEqual(2, projects.Length, "The native loader must expose both target frameworks for this regression.");
                Assert.AreEqual(2, projects.Select(project => project.OutputFilePath).Distinct(FileSystemPath.Comparer).Count());
                var graph = EvaluatedBuildGraph.Source(solution, CancellationToken.None);
                Assert.AreEqual(2, projects.Select(project => EvaluatedBuildGraph.Resolve(project, graph).TargetFramework).Distinct().Count());
                await SolutionFileMutationPlanner.AssertEquivalentAsync(solution, solution, static path => path, static path => path,
                    verifyDocuments: true, BuildPathRelocation.KnownPathProperties, CancellationToken.None);
                await File.WriteAllTextAsync(source.GetPath("ContextOwner.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
                var ownerId = ProjectId.CreateNewId();
                var withOwner = solution.AddProject(ProjectInfo.Create(ownerId, VersionStamp.Create(), "ContextOwner", "ContextOwner",
                    LanguageNames.CSharp, filePath: source.GetPath("ContextOwner.csproj")));
                var firstEdge = withOwner.AddProjectReference(ownerId, new ProjectReference(projects[0].Id));
                var switchedEdge = withOwner.AddProjectReference(ownerId, new ProjectReference(projects[1].Id));
                await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(
                    firstEdge, switchedEdge, static path => path, static path => path, verifyDocuments: true, BuildPathRelocation.KnownPathProperties, CancellationToken.None));
                var policy = new OwnedTestWorkspacePathPolicy();
                var layout = await PhysicalGraphLayout.CreateAsync(loaded.WorkspaceId, projectPath, solution, policy, CancellationToken.None);
                var fork = source.GetPath(".roslynmcp", "forks", "native-contexts");
                var plan = await layout.PlanCopyAsync(loaded.WorkspaceId, fork, policy, CancellationToken.None);
                await policy.ValidateAsync(loaded.WorkspaceId, plan.Inventory, CancellationToken.None);
                var stageOperation = ((IWorkspaceEvaluationLoader)WorkspaceManager).CaptureStageInvocationAuthority(loaded.WorkspaceId, solution);
                var preparation = await ForkGraphPreparation.CreateAsync(loaded.WorkspaceId, projectPath,
                    solution, solution, solution, layout, fork, stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
                Assert.AreEqual(solution.ProjectIds.Count, preparation.Expected.ProjectIds.Count);
                Assert.AreEqual(2, preparation.SourceProjects.Select(project => project.TargetFramework).OfType<string>().Distinct().Count());
                Assert.IsFalse(Directory.Exists(fork), "Context validation must precede copy effects.");
                var token = PreviewStore.Store(loaded.WorkspaceId, solution, solution, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId),
                    "native multi-context fork", [], PreviewKind.Unspecified);
                var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                    TestRunnerService, WorkspaceManager, ChangeTracker);
                var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
                    validation, TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
                WorkspaceForkApplyResultDto? result = null;
                Exception? primary = null;
                try
                {
                    result = await service.ApplyAsync(loaded.WorkspaceId, token, "keep", false, null, "native-contexts", CancellationToken.None);
                    Assert.IsTrue(result.Success, "The restored native fork must validate both evaluated target-framework contexts.");
                    var forkSolution = WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
                    Assert.AreEqual(2, forkSolution.Projects.Count());
                    Assert.IsTrue(forkSolution.Projects.All(project => FileSystemPath.IsStrictDescendant(result.ForkPath, project.FilePath!)));
                    Assert.IsNotNull(PreviewStore.Retrieve(token));
                }
                catch (Exception failure) { primary = failure; throw; }
                finally
                {
                    if (result is not null)
                        await CleanupFailureCollector.RunAfterFailureAsync("Native context fork cleanup failed.", primary,
                            CleanupFailureCollector.FromAction(() => { if (result.ForkWorkspaceId is not null) WorkspaceManager.Close(result.ForkWorkspaceId); }),
                            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath)));
                }
            }
            catch (Exception failure) { closeFailure30 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay source workspace close failed.", closeFailure30,
                    CleanupFailureCollector.FromAction(() => { WorkspaceManager.Close(loaded.WorkspaceId); }));
            }
        }
        catch (Exception failure) { cleanupFailure27 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure27,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeContextRebase_RequiresKnownOutputWhenReloadLeavesOneContext(bool sameFramework)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure94 = null;
        try
        {
            var projectPath = await PrepareNativeMultiTargetProjectAsync(source);
            var loaded = await WorkspaceManager.LoadAsync(projectPath, OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            Exception? closeFailure97 = null;
            try
            {
                var original = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
                Assert.AreEqual(2, original.ProjectIds.Count);
                var originalProject = original.Projects.Single(project => EvaluatedBuildGraph.Resolve(project, CancellationToken.None).TargetFramework == "net10.0");
                var document = originalProject.Documents.Single(document => document.FilePath == source.GetPath("SampleLib", "Dog.cs"));
                var originalText = await document.GetTextAsync();
                var previewText = SourceText.From("// reviewed original framework\n" + originalText, originalText.Encoding);
                var preview = original.WithDocumentText(document.Id, previewText);
                var framework = sameFramework ? "net10.0" : "net10.0-windows";
                await File.WriteAllTextAsync(projectPath,
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>" + framework
                    + "</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
                var restore = await new DotnetCommandRunner().RunAsync(Path.GetDirectoryName(projectPath)!, projectPath,
                    ["restore", "--verbosity", "quiet"], CancellationToken.None);
                Assert.AreEqual(0, restore.ExitCode);
                await WorkspaceManager.ReloadAsync(loaded.WorkspaceId, CancellationToken.None);
                var current = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
                Assert.AreEqual(1, current.ProjectIds.Count);
                Assert.AreEqual(framework, EvaluatedBuildGraph.Resolve(current.Projects.Single(), CancellationToken.None).TargetFramework);
                var bytes = await File.ReadAllBytesAsync(document.FilePath!);
                var version = WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId);
                if (sameFramework)
                {
                    var rebased = await RoslynMcp.Roslyn.Services.RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, preview, current, CancellationToken.None);
                    var target = rebased.Projects.Single().Documents.Single(candidate => candidate.FilePath == document.FilePath);
                    Assert.AreEqual(previewText.ToString(), (await target.GetTextAsync()).ToString());
                    Assert.AreEqual(originalProject.OutputFilePath, rebased.Projects.Single().OutputFilePath);
                }
                else await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                    RoslynMcp.Roslyn.Services.RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, preview, current, CancellationToken.None));
                Assert.AreSame(current, WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(loaded.WorkspaceId));
                CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(document.FilePath!));
            }
            catch (Exception failure) { closeFailure97 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay source workspace close failed.", closeFailure97,
                    CleanupFailureCollector.FromAction(() => { WorkspaceManager.Close(loaded.WorkspaceId); }));
            }
        }
        catch (Exception failure) { cleanupFailure94 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure94,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ForkCopy_PreservesMutableSiblingUnderRecordedPackageAncestor()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure138 = null;
        try
        {
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            var sibling = source.GetPath("MutableCacheSibling.cs");
            await File.WriteAllTextAsync(sibling, "internal sealed class MutableCacheSibling { }");
            var xml = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "../MutableCacheSibling.cs"))));
            await File.WriteAllTextAsync(projectPath, xml.ToString(SaveOptions.DisableFormatting));
            var restored = await new DotnetCommandRunner().RunAsync(Path.GetDirectoryName(projectPath)!, projectPath,
                ["restore", "--packages", source.RootPath, "--verbosity", "quiet"], CancellationToken.None);
            Assert.AreEqual(0, restored.ExitCode);
            var loaded = await WorkspaceManager.LoadAsync(projectPath, OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            Exception? closeFailure149 = null;
            try
            {
                var solution = WorkspaceManager.GetCurrentSolution(loaded.WorkspaceId);
                Assert.IsTrue(solution.Projects.Single().Documents.Any(document => document.FilePath == sibling));
                Assert.IsTrue(RestoreInvocationPlanner.Capture(solution, CancellationToken.None).All(project => FileSystemPath.Comparer.Equals(project.PackagesPath, source.RootPath)),
                    "The recorded package authority must actually be the authored sibling's ancestor.");
                var policy = new OwnedTestWorkspacePathPolicy();
                var layout = await PhysicalGraphLayout.CreateAsync(loaded.WorkspaceId, projectPath, solution, policy, CancellationToken.None);
                Assert.IsFalse(layout.IsImmutablePath(sibling));
                var fork = source.GetPath(".roslynmcp", "forks", "mutable-cache-sibling");
                await policy.ValidateAsync(loaded.WorkspaceId, layout.ContentRoots.Concat(layout.InputFiles).ToArray(), CancellationToken.None);
                var copy = await layout.PlanCopyAsync(loaded.WorkspaceId, fork, policy, CancellationToken.None);
                Assert.IsTrue(copy.Files.Any(file => FileSystemPath.Comparer.Equals(file.Source, sibling)));
                await policy.ValidateAsync(loaded.WorkspaceId, copy.Inventory, CancellationToken.None);
                var stageOperation = ((IWorkspaceEvaluationLoader)WorkspaceManager).CaptureStageInvocationAuthority(loaded.WorkspaceId, solution);
                var preparation = await ForkGraphPreparation.CreateAsync(loaded.WorkspaceId, projectPath,
                    solution, solution, solution, layout, fork, stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
                copy = preparation.CopyPlan;
                Exception? primary = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fork)!);
                    var copyOwner = PhysicalDirectoryScope.CreateOwnedRoot(fork);
                    Exception? cleanupFailure171 = null;
                    try
                    {
                        await copy.CopyAsync(copyOwner, CancellationToken.None);
                        await preparation.ReplayAsync(fork, copyOwner, CancellationToken.None);
                        CollectionAssert.AreEqual(await File.ReadAllBytesAsync(sibling),
                            await File.ReadAllBytesAsync(layout.MapInputPath(fork, sibling)));
                    }
                    catch (Exception failure) { cleanupFailure171 = failure; throw; }
                    finally
                    {
                        await CleanupFailureCollector.RunAfterFailureAsync("Replay copyOwner cleanup failed.", cleanupFailure171,
                            CleanupFailureCollector.FromAction(copyOwner.Dispose));
                    }
                }
                catch (Exception failure) { primary = failure; throw; }
                finally
                {
                    await CleanupFailureCollector.RunAfterFailureAsync("Mutable cache sibling fork cleanup failed.", primary,
                        CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fork)));
                }
            }
            catch (Exception failure) { closeFailure149 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay source workspace close failed.", closeFailure149,
                    CleanupFailureCollector.FromAction(() => { WorkspaceManager.Close(loaded.WorkspaceId); }));
            }
        }
        catch (Exception failure) { cleanupFailure138 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure138,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ForkExecutionAuthority_RejectsRestoreAuthoredInputDrift(bool createNearerConfiguration)
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure192 = null;
        try
        {
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            var status = await WorkspaceManager.LoadAsync(projectPath, OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            WorkspaceForkApplyResultDto? result = null;
            Exception? primary = null;
            try
            {
                var original = WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
                var version = WorkspaceManager.GetCurrentVersion(status.WorkspaceId);
                var before = await File.ReadAllBytesAsync(projectPath);
                Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "global.json")));
                var token = PreviewStore.Store(status.WorkspaceId, original, original, version,
                    "reviewed execution authority", [], PreviewKind.Unspecified);
                var runner = new ForkExecutionMutationRunner(createNearerConfiguration);
                var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                    TestRunnerService, WorkspaceManager, ChangeTracker);
                var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager,
                    WorkspaceExecutionGate, PreviewStore, validation, TestRunnerService, runner,
                    NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(async () =>
                    result = await service.ApplyAsync(status.WorkspaceId, token, "keep", false, null, "execution-drift", CancellationToken.None));
                Assert.AreEqual(1, runner.Mutations);
                Assert.IsNotNull(runner.MutatedPath);
                Assert.IsFalse(File.Exists(runner.MutatedPath), "Rejected forks must be closed and removed before reporting failure.");
                Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(status.WorkspaceId));
                CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(projectPath));
                Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "global.json")));
                Assert.IsNotNull(PreviewStore.Retrieve(token));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Execution authority fork cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => { if (result?.ForkWorkspaceId is { } forkId) WorkspaceManager.Close(forkId); }),
                    CleanupFailureCollector.FromAction(() => { if (result is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath); }),
                    CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(status.WorkspaceId)));
            }
        }
        catch (Exception failure) { cleanupFailure192 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure192,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ContextRebase_RejectsChangedOutputEvenWhenProjectIdIsReused()
    {
        var workspace = new AdhocWorkspace();
        Exception? cleanupFailure235 = null;
        try
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var root = Path.Combine(TestTempRoot.Current, "same-id-context-" + Guid.NewGuid().ToString("N"));
            var original = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Context", "Context",
                LanguageNames.CSharp, filePath: Path.Combine(root, "Context.csproj"), outputFilePath: Path.Combine(root, "net10.0", "Context.dll")))
                .AddDocument(documentId, "Context.cs", SourceText.From("internal class Context {}"), filePath: Path.Combine(root, "Context.cs"));
            var preview = original.WithDocumentText(documentId, SourceText.From("internal class ReviewedContext {}"));
            var current = original.WithProjectOutputFilePath(projectId, Path.Combine(root, "other-context", "Context.dll"));
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                RoslynMcp.Roslyn.Services.RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, preview, current, CancellationToken.None));
        }
        catch (Exception failure) { cleanupFailure235 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay workspace cleanup failed.", cleanupFailure235,
                CleanupFailureCollector.FromAction(workspace.Dispose));
        }
    }

    [TestMethod]
    public async Task AuthoredOutputDelta_RebasesUsingOriginalContextAndRoundTripsThroughUndo()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure251 = null;
        try
        {
            var path = source.GetPath("SampleLib", "SampleLib.csproj");
            var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "SampleLib.csproj"))));
            await File.WriteAllTextAsync(path, xml.ToString(SaveOptions.DisableFormatting));
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var project = original.Projects.Single(project => project.Name == "SampleLib");
            var document = project.AdditionalDocuments.Single(document => document.FilePath == path);
            var originalXml = await File.ReadAllBytesAsync(path);
            var updatedXml = XDocument.Parse((await document.GetTextAsync()).ToString(), LoadOptions.PreserveWhitespace);
            updatedXml.Root!.Add(new XElement("PropertyGroup", new XElement("OutputPath", "bin/Reviewed/")));
            var output = Path.Combine(Path.GetDirectoryName(path)!, "bin", "Reviewed", "net10.0", "SampleLib.dll");
            var modified = original.WithAdditionalDocumentText(document.Id, SourceText.From(updatedXml.ToString(SaveOptions.DisableFormatting)))
                .WithProjectOutputFilePath(project.Id, output);
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified, WorkspaceManager.GetCurrentVersion(source.WorkspaceId),
                "authored output path", changes, PreviewKind.Unspecified);
            var siblingPath = source.GetPath("SampleApp", "OutputSibling.cs");
            var sibling = await FileOperationService.PreviewCreateFileAsync(source.WorkspaceId,
                new CreateFileDto("SampleApp", siblingPath, "namespace SampleApp; internal sealed class OutputSibling { }"), CancellationToken.None);
            Assert.IsTrue((await RefactoringService.ApplyRefactoringAsync(sibling.PreviewToken, "test_apply", CancellationToken.None)).Success);
            var current = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var rebased = await RoslynMcp.Roslyn.Services.RefactoringService.RebaseModifiedSolutionOntoCurrentAsync(original, modified, current, CancellationToken.None);
            Assert.AreEqual(output, rebased.Projects.Single(project => project.FilePath == path).OutputFilePath);
            Assert.IsTrue(rebased.Projects.Single(project => project.Name == "SampleApp").Documents.Any(document => document.FilePath == siblingPath));
            var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(output, WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.FilePath == path).OutputFilePath);
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            CollectionAssert.AreEqual(originalXml, await File.ReadAllBytesAsync(path));
            Assert.IsTrue(File.Exists(siblingPath));
            Assert.AreEqual(project.OutputFilePath, WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.FilePath == path).OutputFilePath);
        }
        catch (Exception failure) { cleanupFailure251 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure251,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task OrdinaryCrossProjectPreview_ReviewsProjectReferenceWritePaths()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure290 = null;
        try
        {
            AddProjectToCopiedSolution(source.RootPath, "Contracts", "net10.0");
            var path = source.GetPath("SampleLib", "Counter.cs");
            await File.WriteAllTextAsync(path, "namespace SampleLib; public class Counter { public int Count() => 1; }");
            await RestoreCopiedSolutionAsync(source.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await source.LoadAsync();
            var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                source.WorkspaceId, path, "Counter", "ICounter", "Contracts", CancellationToken.None);
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            Assert.IsTrue(preview.Changes.Any(change => change.FilePath == projectPath),
                "The reviewed diff must include the project reference that source apply and fork will write.");
            CollectionAssert.Contains(PreviewStore.PeekChangedPaths(preview.PreviewToken)!.ToArray(), projectPath);
            var change = preview.Changes.Single(change => change.FilePath == projectPath);
            StringAssert.Contains(change.UnifiedDiff, "Contracts.csproj");
        }
        catch (Exception failure) { cleanupFailure290 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure290,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task OrdinaryCrossProjectFork_PreservesSiblingAndSourceAuthority()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure309 = null;
        try
        {
            AddProjectToCopiedSolution(source.RootPath, "Contracts", "net10.0");
            var path = source.GetPath("SampleLib", "Counter.cs");
            await File.WriteAllTextAsync(path, "namespace SampleLib; public class Counter { public int Count() => 1; }");
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            await File.WriteAllTextAsync(projectPath, await File.ReadAllTextAsync(projectPath), Encoding.Unicode);
            await RestoreCopiedSolutionAsync(source.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await source.LoadAsync();
            var preview = await CrossProjectRefactoringService.PreviewExtractInterfaceAsync(
                source.WorkspaceId, path, "Counter", "ICounter", "Contracts", CancellationToken.None);
            var siblingPath = source.GetPath("SampleApp", "Sibling.cs");
            var sibling = await FileOperationService.PreviewCreateFileAsync(source.WorkspaceId,
                new CreateFileDto("SampleApp", siblingPath, "namespace SampleApp; internal class Sibling { }"), CancellationToken.None);
            Assert.IsTrue((await RefactoringService.ApplyRefactoringAsync(sibling.PreviewToken, "test_apply", CancellationToken.None)).Success);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            var sourceProjectBytes = await File.ReadAllBytesAsync(projectPath);
            var sourceCode = await File.ReadAllBytesAsync(path);
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
                validation, TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            WorkspaceForkApplyResultDto? result = null;
            Exception? primary = null;
            try
            {
                result = await service.ApplyAsync(source.WorkspaceId, preview.PreviewToken, "keep", false, null, null, CancellationToken.None);
                var forkProjectPath = Path.Combine(result.ForkPath, "SampleLib", "SampleLib.csproj");
                var xml = XDocument.Load(forkProjectPath);
                Assert.IsTrue(xml.Descendants("ProjectReference").Any(element =>
                    ((string?)element.Attribute("Include"))?.Contains("Contracts.csproj", StringComparison.Ordinal) == true));
                var forkSolution = WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
                var lib = forkSolution.Projects.Single(project => project.Name == "SampleLib");
                Assert.IsTrue(lib.ProjectReferences.Any(reference => forkSolution.GetProject(reference.ProjectId)?.Name == "Contracts"));
                Assert.IsTrue(forkSolution.Projects.Single(project => project.Name == "SampleApp").Documents.Any(document => document.Name == "Sibling.cs"));
                Assert.IsTrue(File.Exists(Path.Combine(result.ForkPath, "SampleApp", "Sibling.cs")));
                Assert.IsTrue(CsprojSemanticEquality.CreateSnapshot(await File.ReadAllBytesAsync(forkProjectPath)).HasPreamble);
                Assert.AreEqual(Encoding.Unicode.CodePage, CsprojSemanticEquality.CreateSnapshot(await File.ReadAllBytesAsync(forkProjectPath)).TextEncoding.CodePage);
                Assert.IsTrue(result.Success, "The actual fork graph must compile with its reviewed reference edge. "
                    + "Status=" + result.Validation.OverallStatus + "; compile=" + result.Validation.CompileResult.Success
                    + "; errors=" + result.Validation.ErrorCount + "; warnings=" + result.Validation.WarningCount
                    + "; compile diagnostics=" + string.Join("; ", result.Validation.CompileResult.Diagnostics.Select(diagnostic =>
                        diagnostic.Id + " [" + Path.GetFileName(diagnostic.FilePath) + "]: " + diagnostic.Message))
                    + "; compile elapsedMs=" + result.Validation.CompileResult.ElapsedMs
                    + "; compile cancelled=" + result.Validation.CompileResult.Cancelled
                    + "; compile projects=" + result.Validation.CompileResult.CompletedProjects + "/" + result.Validation.CompileResult.TotalProjects
                    + "; compile readiness=" + result.Validation.CompileResult.Readiness
                    + "; diagnostics=" + string.Join("; ", result.Validation.ErrorDiagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.Message))
                    + "; validation warnings=" + string.Join("; ", result.Validation.Warnings));
                CollectionAssert.AreEqual(sourceProjectBytes, await File.ReadAllBytesAsync(projectPath));
                CollectionAssert.AreEqual(sourceCode, await File.ReadAllBytesAsync(path));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
                Assert.IsNotNull(PreviewStore.Retrieve(preview.PreviewToken));
                var applied = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
                Assert.IsTrue(applied.Success, applied.Error);
                Assert.IsTrue(File.Exists(siblingPath));
                Assert.IsTrue(XDocument.Load(projectPath).Descendants("ProjectReference").Any(element =>
                    ((string?)element.Attribute("Include"))?.Contains("Contracts.csproj", StringComparison.Ordinal) == true));
            }
            catch (Exception ex) { primary = ex; WriteSafeGraphDiagnostics(ex); throw; }
            finally
            {
                if (result is not null)
                    await CleanupFailureCollector.RunAfterFailureAsync("Fork graph cleanup failed.", primary,
                        CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(result.ForkWorkspaceId!)),
                        CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath)));
            }
        }
        catch (Exception failure) { cleanupFailure309 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure309,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ReferenceMetadataReplacement_IsReviewedAndPersistsWithoutLosingTheEdge()
    {
        var source = await CreateInteropWorkspaceAsync();
        Exception? cleanupFailure380 = null;
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var app = original.Projects.Single(project => project.Name == "SampleApp");
            var reference = app.ProjectReferences.Single(reference => original.GetProject(reference.ProjectId)!.Name == "Interop");
            var modified = original.RemoveProjectReference(app.Id, reference)
                .AddProjectReference(app.Id, new ProjectReference(reference.ProjectId, ["global", "library"], embedInteropTypes: true));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "replace reference metadata", changes, PreviewKind.Unspecified);
            var siblingDocument = app.Documents.First(document => document.FilePath!.EndsWith(".cs", StringComparison.Ordinal));
            var siblingText = await siblingDocument.GetTextAsync();
            var siblingModified = original.WithDocumentText(siblingDocument.Id,
                SourceText.From(siblingText + "\n// sibling across graph publication\n", siblingText.Encoding));
            var siblingChanges = await SolutionDiffHelper.ComputeChangesAsync(original, siblingModified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var siblingToken = PreviewStore.Store(source.WorkspaceId, original, siblingModified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "sibling before graph", siblingChanges, PreviewKind.Unspecified);
            var logger = new CaptureLogger<RefactoringService>();
            var refactoring = new RefactoringService(WorkspaceManager, PreviewStore, logger, UndoService, ChangeTracker);
            var result = await refactoring.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error + "\n" + string.Join("\n", logger.Entries.Select(entry => entry.Exception?.ToString())));
            var graphSequence = ChangeTracker.GetChanges(source.WorkspaceId).Single(change => change.Description == "replace reference metadata").SequenceNumber;
            var xml = XDocument.Load(app.FilePath!);
            var item = xml.Descendants("ProjectReference").Single(item => ((string?)item.Attribute("Include"))?.Contains("Interop.csproj", StringComparison.Ordinal) == true);
            Assert.AreEqual("global,library", item.Element("Aliases")?.Value);
            Assert.AreEqual("true", item.Element("EmbedInteropTypes")?.Value);
            var loaded = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.IsTrue(loaded.Projects.Single(project => project.Name == "SampleApp").ProjectReferences.Single(reference => loaded.GetProject(reference.ProjectId)!.Name == "Interop").EmbedInteropTypes);
            var document = loaded.Projects.Single(project => project.Name == "SampleLib").Documents.First(document => document.FilePath!.EndsWith(".cs", StringComparison.Ordinal));
            var text = await document.GetTextAsync();
            var next = loaded.WithDocumentText(document.Id, SourceText.From(text.ToString() + "\n// retained interop metadata\n", text.Encoding));
            var nextChanges = await SolutionDiffHelper.ComputeChangesAsync(loaded, next, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var nextToken = PreviewStore.Store(source.WorkspaceId, loaded, next, WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "text after metadata", nextChanges, PreviewKind.Unspecified);
            var captured = PreviewStore.Retrieve(nextToken)!.Value.OriginalSolution;
            Assert.IsTrue(captured.Projects.Single(project => project.Name == "SampleApp").ProjectReferences.Single(reference => captured.GetProject(reference.ProjectId)!.Name == "Interop").EmbedInteropTypes);
            Assert.IsTrue((await refactoring.ApplyRefactoringAsync(nextToken, "test_apply", CancellationToken.None)).Success);
            var afterText = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.IsTrue(afterText.Projects.Single(project => project.Name == "SampleApp").ProjectReferences.Single(reference => afterText.GetProject(reference.ProjectId)!.Name == "Interop").EmbedInteropTypes);
            Assert.AreEqual("true", XDocument.Load(app.FilePath!).Descendants("EmbedInteropTypes").Single().Value);
            Assert.IsNotNull(PreviewStore.Retrieve(siblingToken));
            Assert.IsTrue((await refactoring.ApplyRefactoringAsync(siblingToken, "test_apply", CancellationToken.None)).Success);
            StringAssert.Contains(await File.ReadAllTextAsync(siblingDocument.FilePath!), "// sibling across graph publication");
            var versionBeforeUndo = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            Assert.IsTrue((await UndoService.RevertBySequenceAsync(source.WorkspaceId, graphSequence)).Reverted);
            Assert.AreEqual(versionBeforeUndo + 1, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            var reverted = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.IsFalse(reverted.Projects.Single(project => project.Name == "SampleApp").ProjectReferences
                .Single(reference => reverted.GetProject(reference.ProjectId)!.Name == "Interop").EmbedInteropTypes);
            StringAssert.Contains(await File.ReadAllTextAsync(siblingDocument.FilePath!), "// sibling across graph publication");
            StringAssert.Contains(await File.ReadAllTextAsync(document.FilePath!), "// retained interop metadata");
        }
        catch (Exception failure) { cleanupFailure380 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure380,
                () => source.DisposeAsync());
        }
    }
    [TestMethod]
    public async Task WrongPersistedMetadata_PreservesUnknownBytesAndRetainsRecoveryAuthority()
    {
        var source = await CreateInteropWorkspaceAsync();
        Exception? primary = null;
        byte[]? intendedBytes = null;
        try
        {
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var app = original.Projects.Single(project => project.Name == "SampleApp");
            var reference = app.ProjectReferences.Single(reference => original.GetProject(reference.ProjectId)!.Name == "Interop");
            var modified = original.RemoveProjectReference(app.Id, reference)
                .AddProjectReference(app.Id, new ProjectReference(reference.ProjectId, ["global", "library"], true));
            var before = await File.ReadAllBytesAsync(app.FilePath!);
            var version = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            await PublishPreviousExternalUndoAsync(WorkspaceManager, UndoService, source.WorkspaceId, source.GetPath(".previous-undo"), "previous successful operation");
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified, version, "candidate must fail", changes, PreviewKind.Unspecified);
            var sibling = PreviewStore.Store(source.WorkspaceId, original, original, version, "sibling remains redeemable", [], PreviewKind.Unspecified);
            var recoveryAuthority = (IWorkspaceRecoveryAuthority)WorkspaceManager;
            byte[]? wrongBytes = null;
            var injected = false;
            var wrapper = FailClosedWorkspaceManagerStub.ForRecovery(WorkspaceManager,
                recoveryAuthority.AuthorizePathsAsync, recoveryAuthority.StageRestorationAsync,
                async (physical, expected, retainedRecovery, ct) =>
                {
                    injected = true;
                    intendedBytes = await File.ReadAllBytesAsync(app.FilePath!, ct);
                    var text = await File.ReadAllTextAsync(app.FilePath!, ct);
                    StringAssert.Contains(text, "<EmbedInteropTypes>true</EmbedInteropTypes>");
                    await File.WriteAllTextAsync(app.FilePath!, text.Replace("<EmbedInteropTypes>true</EmbedInteropTypes>",
                        "<EmbedInteropTypes>false</EmbedInteropTypes>", StringComparison.Ordinal), ct);
                    wrongBytes = await File.ReadAllBytesAsync(app.FilePath!, ct);
                    Assert.IsFalse(intendedBytes.AsSpan().SequenceEqual(wrongBytes), "Fixture must change persisted metadata before native publication.");
                    await recoveryAuthority.CommitRestorationAsync(physical, expected, retainedRecovery, ct);
                });
            var logger = new CaptureLogger<RefactoringService>();
            var service = new RefactoringService(wrapper, PreviewStore, logger, UndoService, ChangeTracker);
            var failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => service.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None));
            Assert.IsTrue(injected, "The native restoration publication boundary must execute the metadata mutation.");
            Assert.IsTrue(failure.Flatten().InnerExceptions.Any(error => error is PublicInvalidOperationException
                && error.Message == "Physical execution inputs changed after preparation; retry against the current source."),
                "The real publication boundary must reject the changed project bytes before evaluating the candidate graph. Actual: " +
                string.Join(" | ", failure.Flatten().InnerExceptions.Select(error => error.GetType().Name + ": " + error.Message)));
            Assert.IsNotNull(wrongBytes);
            CollectionAssert.AreEqual(wrongBytes, await File.ReadAllBytesAsync(app.FilePath!));
            Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(source.WorkspaceId));
            Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            Assert.IsNotNull(recoveryAuthority.GetPendingRecovery(source.WorkspaceId));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => recoveryAuthority.EnsureMutationAllowed(source.WorkspaceId, [app.FilePath!]));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
            Assert.IsNotNull(PreviewStore.Retrieve(sibling));
            Assert.IsNotNull(intendedBytes);
            await File.WriteAllBytesAsync(app.FilePath!, intendedBytes); // Explicit fixture resolution to registered intended bytes.
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(app.FilePath!));
            Assert.IsNull(recoveryAuthority.GetPendingRecovery(source.WorkspaceId));
            Assert.AreEqual("previous successful operation", UndoService.GetLastOperation(source.WorkspaceId)?.Description);
            Assert.IsTrue((await RefactoringService.ApplyRefactoringAsync(sibling, "test_apply", CancellationToken.None)).Success);
            Assert.IsTrue((await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None)).Success);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Wrong persisted metadata fixture cleanup failed.", primary,
                async () =>
                {
                    if (((IWorkspaceRecoveryAuthority)WorkspaceManager).GetPendingRecovery(source.WorkspaceId) is null || intendedBytes is null) return;
                    var appPath = WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.Name == "SampleApp").FilePath!;
                    await File.WriteAllBytesAsync(appPath, intendedBytes);
                    Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
                }, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ProjectFileDocumentAndReferenceDelta_AreReviewedAppliedAndUndoneTogether()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure508 = null;
        try
        {
            AddProjectToCopiedSolution(source.RootPath, "Contracts", "net10.0");
            var path = source.GetPath("SampleLib", "SampleLib.csproj");
            var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            xml.Root!.Add(new XElement("ItemGroup", new XElement("AdditionalFiles", new XAttribute("Include", "SampleLib.csproj"))));
            await File.WriteAllTextAsync(path, xml.ToString(SaveOptions.DisableFormatting));
            await RestoreCopiedSolutionAsync(source.SolutionPath, new Dictionary<string, string>(), CancellationToken.None);
            await source.LoadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var before = await File.ReadAllBytesAsync(path);
            var library = original.Projects.Single(project => project.Name == "SampleLib");
            var contracts = original.Projects.Single(project => project.Name == "Contracts");
            var document = library.AdditionalDocuments.Single(document => document.FilePath == path);
            var text = await document.GetTextAsync();
            var modified = original.WithAdditionalDocumentText(document.Id,
                    SourceText.From(text + "\n<!-- authored project note -->\n", text.Encoding))
                .AddProjectReference(library.Id, new ProjectReference(contracts.Id));
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var reviewed = changes.Single(change => change.FilePath == path).UnifiedDiff;
            StringAssert.Contains(reviewed, "authored project note");
            StringAssert.Contains(reviewed, "Contracts.csproj");
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "combined physical project delta", changes, PreviewKind.Unspecified);
            var result = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(result.Success, result.Error);
            var persisted = await File.ReadAllTextAsync(path);
            StringAssert.Contains(persisted, "authored project note");
            StringAssert.Contains(persisted, "Contracts.csproj");
            var current = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            Assert.AreEqual(persisted, (await current.Projects.Single(project => project.Name == "SampleLib")
                .AdditionalDocuments.Single(document => document.FilePath == path).GetTextAsync()).ToString());
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
        }
        catch (Exception failure) { cleanupFailure508 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure508,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ProjectEntryLayout_MapsSiblingWithoutCopyingUnrelatedCommonParent()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? primary = null;
        try
        {
            var projectPath = source.GetPath("SampleApp", "SampleApp.csproj");
            var status = await WorkspaceManager.LoadAsync(projectPath, OwnedNativeLoadContext.Properties(projectPath, new Dictionary<string, string>()), CancellationToken.None);
            Exception? closeFailure552 = null;
            try
            {
                var layout = await PhysicalGraphLayout.CreateAsync(status.WorkspaceId, projectPath, WorkspaceManager.GetCurrentSolution(status.WorkspaceId), new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
                Assert.AreEqual(Path.GetFullPath(source.RootPath), layout.Root);
                CollectionAssert.Contains(layout.ContentRoots.ToArray(), Path.GetDirectoryName(projectPath)!);
                CollectionAssert.Contains(layout.ContentRoots.ToArray(), source.GetPath("SampleLib"));
                Assert.IsFalse(layout.ContentRoots.Contains(source.RootPath, FileSystemPath.Comparer), "The common layout ancestor is not an authorized recursive content root.");
                var unrelated = source.GetPath("UnrelatedSibling", "NeverCopy.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
                await File.WriteAllTextAsync(unrelated, "// unrelated source tree");
                var target = source.GetPath(".roslynmcp", "layout-probe");
                var plan = await layout.PlanCopyAsync(status.WorkspaceId, target, new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
                Assert.IsFalse(plan.Files.Any(file => FileSystemPath.Comparer.Equals(file.Source, unrelated)));
                Assert.IsTrue(plan.Files.Any(file => FileSystemPath.Comparer.Equals(file.Source, source.GetPath("SampleLib", "Dog.cs"))));
                Assert.IsFalse(Directory.Exists(target), "Planning and root refusal must precede destination effects.");
            }
            catch (Exception failure) { closeFailure552 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay source workspace close failed.", closeFailure552,
                    CleanupFailureCollector.FromAction(() => { WorkspaceManager.Close(status.WorkspaceId); }));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Project layout cleanup failed.", primary, () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public void ProvenPathGlobal_RelocatesEscapedSemanticPathOnly()
    {
        var source = Path.Combine(Path.GetTempPath(), "source;graph");
        var fork = Path.Combine(Path.GetTempPath(), "fork;graph");
        var value = Path.Combine(source, "obj").Replace(";", "%3b", StringComparison.Ordinal);
        Assert.AreEqual(Path.Combine(fork, "obj").Replace(";", "%3b", StringComparison.Ordinal),
            BuildPathRelocation.RelocateGlobal("OutputPath", value, path => path.Replace(source, fork, StringComparison.Ordinal), BuildPathRelocation.KnownPathProperties));
        const string untouched = "literal%253Bidentity";
        Assert.AreEqual(untouched, BuildPathRelocation.RelocateGlobal("OutputPath", untouched,
            path => path.Replace(source, fork, StringComparison.Ordinal), BuildPathRelocation.KnownPathProperties));
    }
    [TestMethod]
    public async Task MixedRestoreRoots_UseDependencyOrderWithIndependentPackageAuthority()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure592 = null;
        try
        {
            var consumerPath = source.GetPath("RestoreConsumer", "Consumer.csproj");
            var dependencyPath = source.GetPath("RestoreDependency", "Dependency.csproj");
            var consumerRoot = source.GetPath("ConsumerPackages");
            var dependencyRoot = source.GetPath("DependencyPackages");
            foreach (var (path, packages) in new[] { (consumerPath, consumerRoot), (dependencyPath, dependencyRoot) })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var project = new XElement("Project", new XElement("PropertyGroup",
                    new XElement("ProjectAssetsFile", "obj/project.assets.json")));
                if (FileSystemPath.Comparer.Equals(path, consumerPath))
                    project.Add(new XElement("ItemGroup", new XElement("ProjectReference",
                        new XAttribute("Include", Path.GetRelativePath(Path.GetDirectoryName(consumerPath)!, dependencyPath)))));
                await File.WriteAllTextAsync(path, project.ToString());
                Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "obj"));
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!, "obj", "project.assets.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { project = new { restore = new { packagesPath = packages } } }));
            }
            var workspace = new AdhocWorkspace();
            Exception? cleanupFailure610 = null;
            try
            {
                var consumerId = ProjectId.CreateNewId();
                var dependencyId = ProjectId.CreateNewId();
                var solution = workspace.CurrentSolution
                    .AddProject(ProjectInfo.Create(consumerId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp, filePath: consumerPath))
                    .AddProject(ProjectInfo.Create(dependencyId, VersionStamp.Create(), "Dependency", "Dependency", LanguageNames.CSharp, filePath: dependencyPath))
                    .AddProjectReference(consumerId, new ProjectReference(dependencyId));
                var captured = RestoreInvocationPlanner.Capture(solution, CancellationToken.None);
                CollectionAssert.AreEqual(new[] { dependencyPath, consumerPath }, captured.Select(item => item.ProjectPath).ToArray());
                CollectionAssert.AreEqual(new[] { dependencyRoot, consumerRoot }, captured.Select(item => item.PackagesPath).ToArray());
                var plan = RestoreInvocationPlanner.Plan(source.SolutionPath, captured);
                Assert.IsTrue(plan.All(invocation => invocation.Arguments.Contains("--no-dependencies")));
                CollectionAssert.AreEqual(new[] { dependencyPath, consumerPath }, plan.Select(item => item.TargetPath).ToArray());
            }
            catch (Exception failure) { cleanupFailure610 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay workspace cleanup failed.", cleanupFailure610,
                    CleanupFailureCollector.FromAction(workspace.Dispose));
            }
        }
        catch (Exception failure) { cleanupFailure592 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure592,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task ConditionalForkGraph_PreservesEvaluationPathsIdentityAndActualTestExecution()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure628 = null;
        try
        {
            AddProjectToCopiedSolution(source.RootPath, "Contracts", "net10.0");
            var projectPath = source.GetPath("SampleLib", "SampleLib.csproj");
            var xml = XDocument.Load(projectPath);
            const string condition = "'$(GraphVariant)' == 'enabled'";
            xml.Root!.Add(new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", "$(GraphRoot)/Contracts.csproj"), new XAttribute("Condition", condition)),
                new XElement("Compile", new XAttribute("Include", "Variant.conditional"), new XAttribute("Condition", condition)),
                new XElement("CompilerVisibleProperty", new XAttribute("Include", "GraphIdentity")),
                new XElement("CompilerVisibleProperty", new XAttribute("Include", "GraphRoot")),
                new XElement("AssemblyAttribute", new XAttribute("Include", "System.Reflection.AssemblyMetadataAttribute"),
                    new XElement("_Parameter1", "GraphRootIdentity"), new XElement("_Parameter2", "$(GraphRoot)")),
                new XElement("AssemblyAttribute", new XAttribute("Include", "System.Reflection.AssemblyMetadataAttribute"),
                    new XElement("_Parameter1", "GraphIdentity"), new XElement("_Parameter2", "$(GraphIdentity)"))));
            await File.WriteAllTextAsync(projectPath, xml.ToString());
            await File.WriteAllTextAsync(source.GetPath("SampleLib", "Variant.conditional"),
                "namespace SampleLib; public static class VariantOnly { public const string Value = \"enabled\"; }");
            var identity = source.GetPath("identity-is-semantic");
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(identity, quote: true);
            var graphRoot = source.GetPath("Contracts");
            var graphRootLiteral = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(graphRoot, quote: true);
            await File.WriteAllTextAsync(source.GetPath("SampleLib.Tests", "ForkPropertyGraphTests.cs"), $$"""
                using System.Linq;
                using System.Reflection;
                using Microsoft.VisualStudio.TestTools.UnitTesting;
                [TestClass] public class ForkPropertyGraphTests
                {
                    [TestMethod] public void CapturedGraphIsExecuted()
                    {
                        Assert.AreEqual("enabled", SampleLib.VariantOnly.Value);
                        Assert.AreEqual({{literal}}, typeof(SampleLib.VariantOnly).Assembly
                            .GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "GraphIdentity").Value);
                        Assert.AreEqual({{graphRootLiteral}}, typeof(SampleLib.VariantOnly).Assembly
                            .GetCustomAttributes<AssemblyMetadataAttribute>().Single(item => item.Key == "GraphRootIdentity").Value);
                    }
                }
                """);
            var properties = new Dictionary<string, string>
            {
                ["GraphVariant"] = "enabled",
                ["GraphRoot"] = graphRoot,
                ["GraphIdentity"] = identity,
                ["Configuration"] = "Release"
            };
            await RestoreCopiedSolutionAsync(source.SolutionPath, properties, CancellationToken.None);
            var status = await WorkspaceManager.LoadAsync(source.SolutionPath, OwnedNativeLoadContext.Properties(source.SolutionPath, properties), CancellationToken.None);
            WorkspaceForkApplyResultDto? result = null;
            Exception? primary = null;
            try
            {
                var original = WorkspaceManager.GetCurrentSolution(status.WorkspaceId);
                var library = original.Projects.Single(project => project.Name == "SampleLib");
                Assert.IsTrue(library.ProjectReferences.Any(reference => original.GetProject(reference.ProjectId)!.Name == "Contracts"));
                var document = library.Documents.Single(document => document.Name == "Variant.conditional");
                var text = await document.GetTextAsync();
                var modified = original.WithDocumentText(document.Id, SourceText.From(text + "\n// reviewed edit\n", text.Encoding));
                var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified, SolutionDiffReadContext.ForWorkspace(WorkspaceManager, status.WorkspaceId), CancellationToken.None);
                var version = WorkspaceManager.GetCurrentVersion(status.WorkspaceId);
                var token = PreviewStore.Store(status.WorkspaceId, original, modified, version, "conditional graph", changes, PreviewKind.Unspecified);
                var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                    TestRunnerService, WorkspaceManager, ChangeTracker);
                var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager, WorkspaceExecutionGate, PreviewStore,
                    validation, TestRunnerService, new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
                result = await service.ApplyAsync(status.WorkspaceId, token, "keep", true,
                    "FullyQualifiedName~ForkPropertyGraphTests", null, CancellationToken.None);
                Assert.IsTrue(result.Success);
                Assert.IsNotNull(result.ExplicitTestRun);
                Assert.AreEqual(1, result.ExplicitTestRun.Total);
                Assert.AreEqual(1, result.ExplicitTestRun.Passed);
                var fork = WorkspaceManager.GetCurrentSolution(result.ForkWorkspaceId!);
                var forkProperties = EvaluatedProjectGraph.Properties(fork);
                Assert.AreEqual(identity, forkProperties["GraphIdentity"]);
                Assert.AreEqual(graphRoot, forkProperties["GraphRoot"],
                    "A custom global used as semantic payload must retain its captured value; relocate its proven physical consumers instead.");
                var forkLibrary = fork.Projects.Single(project => project.Name == "SampleLib");
                var forkContractPath = Path.Combine(result.ForkPath, "Contracts", "Contracts.csproj");
                var nativeReferences = EvaluatedProjectGraph.References(forkLibrary, forkProperties);
                var contractReferences = nativeReferences.Where(reference =>
                    string.Equals(Path.GetFileName(reference.TargetPath), "Contracts.csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.AreEqual(1, contractReferences.Length, "The active native graph must retain exactly one Contracts edge.");
                Assert.IsTrue(FileSystemPath.Comparer.Equals(forkContractPath, contractReferences[0].TargetPath),
                    "The actual native ProjectReference consumer must target the private fork, despite the unchanged custom global.");
                Assert.IsFalse(nativeReferences.Any(reference => FileSystemPath.Comparer.Equals(
                    source.GetPath("Contracts", "Contracts.csproj"), reference.TargetPath)),
                    "No active native edge may leak back to the original Contracts project.");
                var compiledContractPaths = forkLibrary.ProjectReferences.Select(reference => fork.GetProject(reference.ProjectId)!.FilePath).ToArray();
                Assert.IsTrue(compiledContractPaths.Any(path => FileSystemPath.Comparer.Equals(forkContractPath, path)),
                    "The loaded compiler edge must agree with the independently evaluated private native edge.");
                Assert.AreEqual("Release", forkProperties["Configuration"]);
                Assert.IsTrue(fork.Projects.Single(project => project.Name == "SampleLib").Documents.Any(document => document.Name == "Variant.conditional"));
                Assert.AreSame(original, WorkspaceManager.GetCurrentSolution(status.WorkspaceId));
                Assert.AreEqual(version, WorkspaceManager.GetCurrentVersion(status.WorkspaceId));
                Assert.IsNotNull(PreviewStore.Retrieve(token));
            }
            catch (Exception failure) { primary = failure; WriteSafeGraphDiagnostics(failure); throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Conditional fork cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(status.WorkspaceId)),
                    CleanupFailureCollector.FromAction(() => { if (result?.ForkWorkspaceId is { } id) WorkspaceManager.Close(id); }),
                    CleanupFailureCollector.FromAction(() => { if (result is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(result.ForkPath); }));
            }
        }
        catch (Exception failure) { cleanupFailure628 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure628,
                () => source.DisposeAsync());
        }
    }

    [TestMethod]
    public async Task PropertyDrivenLoadDiagnostic_ClassifiesRawThenRedactsValuesEverywhere()
    {
        var source = CreateIsolatedWorkspaceCopy();
        Exception? cleanupFailure735 = null;
        try
        {
            const string probeValue = "inert-pruned-diagnostic-probe-26a19d";
            var path = source.GetPath("SampleLib", "SampleLib.csproj");
            var xml = XDocument.Load(path);
            xml.Root!.Add(new XElement("Target", new XAttribute("Name", "PropertyDiagnosticProbe"),
                new XAttribute("BeforeTargets", "CoreCompile"),
                new XElement("Error", new XAttribute("Text", "Property probe: $(DiagnosticProbeValue)"))));
            await File.WriteAllTextAsync(path, xml.ToString());
            var logger = new CaptureLogger<WorkspaceManager>();
            var watcher = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            WorkspaceManager? manager = null;
            Exception? cleanupFailure744 = null;
            try
            {
                manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()), logger, new PreviewStore(),
                    watcher, new WorkspaceManagerOptions());
                var status = await manager.LoadAsync(source.SolutionPath,
                    OwnedNativeLoadContext.Properties(source.SolutionPath, new Dictionary<string, string> { ["DiagnosticProbeValue"] = probeValue }), CancellationToken.None);
                var diagnostic = status.WorkspaceDiagnostics.FirstOrDefault(diagnostic => diagnostic.Message.Contains("Property probe:", StringComparison.Ordinal));
                Assert.IsNotNull(diagnostic, "The real MSBuild design-time target must emit the probe diagnostic.");
                Assert.AreEqual("Warning", diagnostic.Severity, "Raw 'pruned' classification must happen before captured value redaction.");
                StringAssert.Contains(diagnostic.Message, "[redacted]");
                Assert.IsFalse(status.WorkspaceDiagnostics.Any(diagnostic => diagnostic.Message.Contains(probeValue, StringComparison.Ordinal)));
                Assert.IsFalse(logger.Entries.Any(entry => entry.Message.Contains(probeValue, StringComparison.Ordinal)
                    || (entry.Exception?.ToString().Contains(probeValue, StringComparison.Ordinal) ?? false)));
            }
            catch (Exception failure) { cleanupFailure744 = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Replay manager cleanup failed.", cleanupFailure744,
                    CleanupFailureCollector.FromAction(() => { if (manager is not null) manager.Dispose(); else watcher.Dispose(); }));
            }
        }
        catch (Exception failure) { cleanupFailure735 = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Replay source cleanup failed.", cleanupFailure735,
                () => source.DisposeAsync());
        }
    }
}
