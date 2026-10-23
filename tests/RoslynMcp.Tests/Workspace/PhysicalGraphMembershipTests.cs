using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphMembershipTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();
    [TestMethod]
    [DataRow(".sln")]
    [DataRow(".slnx")]
    public async Task RemovedMembershipFork_RestoresOnlyTheReviewedGraph(string extension)
    {
        var source = await CreateMembershipFixtureAsync(extension, recordRestoreCoordinates: false);
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            var projectA = Path.Combine(source.RootPath, "A", "A.csproj");
            var packagesA = Path.Combine(source.RootPath, "packages-a");
            var restore = await DotnetCommandRunner.RunAsync(source.RootPath, projectA,
                ["restore", projectA, "--packages", packagesA, "--no-dependencies", "--nologo"], CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, restore.StdErr);
            await source.ReloadAsync();
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var modified = original.RemoveProject(original.Projects.Single(project => project.Name == "B").Id);
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "fork without member B", changes, PreviewKind.Unspecified);
            var sourceBytes = await File.ReadAllBytesAsync(source.SolutionPath);
            var runner = new RecordingRestoreRunner();
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager,
                WorkspaceExecutionGate, PreviewStore, validation, TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            fork = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null,
                "removed-member", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            Assert.IsNotEmpty(runner.Targets);
            Assert.IsFalse(runner.Targets.Any(path => Path.GetFileName(path).Equals("B.csproj", StringComparison.OrdinalIgnoreCase)),
                "Restore must not run a project removed by the reviewed solution-membership delta.");
            Assert.HasCount(1, WorkspaceManager.GetCurrentSolution(fork.ForkWorkspaceId!).Projects.ToArray());
            CollectionAssert.AreEqual(sourceBytes, await File.ReadAllBytesAsync(source.SolutionPath));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var cleanup = new List<Func<ValueTask>>();
            if (fork is not null)
            {
                cleanup.Add(CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(fork.ForkWorkspaceId!)));
                cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath)));
            }
            cleanup.Add(() => source.DisposeAsync());
            await CleanupFailureCollector.RunAfterFailureAsync("Membership fork cleanup failed.", primary, [.. cleanup]);
        }
    }

    [TestMethod]
    public async Task IsolatedPerProjectRestore_UsesForkSolutionProtocolCoordinates()
    {
        var source = await CreateMembershipFixtureAsync(".slnx", recordRestoreCoordinates: true);
        WorkspaceForkApplyResultDto? fork = null;
        Exception? primary = null;
        try
        {
            var projectA = Path.Combine(source.RootPath, "A", "A.csproj");
            var sourceProject = WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.Name == "A");
            var sourceContext = EvaluatedBuildGraph.Resolve(sourceProject, CancellationToken.None);
            var restore = await DotnetCommandRunner.RunAsync(source.RootPath, projectA,
                MsBuildCommandProperties.Append(["restore", projectA, "--packages", Path.Combine(source.RootPath, "packages-a"), "--no-dependencies", "--nologo"], sourceContext.Globals), CancellationToken.None);
            Assert.IsTrue(restore.Succeeded, "Known fixture restore: " + restore.StdOut + "\n" + restore.StdErr);
            await source.ReloadAsync();
            var sourceMarker = Path.Combine(source.RootPath, "fork-protocol.txt");
            File.Delete(sourceMarker);
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var token = PreviewStore.Store(source.WorkspaceId, original, original,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "preserve isolated solution coordinates", [], PreviewKind.Unspecified);
            var validation = new WorkspaceValidationService(CompileCheckService, DiagnosticService, TestDiscoveryService,
                TestRunnerService, WorkspaceManager, ChangeTracker);
            var runner = new RecordingRestoreRunner();
            var service = new WorkspaceForkApplyService(PathPolicy, WorkspaceManager,
                WorkspaceExecutionGate, PreviewStore, validation, TestRunnerService, runner, NullLogger<WorkspaceForkApplyService>.Instance, PhysicalGraphNamespace.ForkParent, TimeProvider.System);
            fork = await service.ApplyAsync(source.WorkspaceId, token, "keep", false, null,
                "solution-coordinates", CancellationToken.None);
            Assert.IsTrue(fork.Success);
            Assert.HasCount(2, runner.Targets);
            Assert.IsTrue(runner.Targets.All(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(File.Exists(sourceMarker), "A captured solution protocol property must not redirect a fork restore target into source.");
            Assert.IsTrue(File.Exists(Path.Combine(fork.ForkPath, "fork-protocol.txt")));
            Assert.IsNotNull(PreviewStore.Retrieve(token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            var cleanup = new List<Func<ValueTask>>();
            if (fork is not null)
            {
                cleanup.Add(CleanupFailureCollector.FromAction(() => WorkspaceManager.Close(fork.ForkWorkspaceId!)));
                cleanup.Add(CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(fork.ForkPath)));
            }
            cleanup.Add(() => source.DisposeAsync());
            await CleanupFailureCollector.RunAfterFailureAsync("Solution protocol fixture cleanup failed.", primary, [.. cleanup]);
        }
    }

    private sealed class RecordingRestoreRunner : IDotnetCommandRunner
    {
        internal List<string> Targets { get; } = [];
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments, CancellationToken ct) =>
            RunAsync(workingDirectory, targetPath, arguments, null, ct);
        public Task<CommandExecutionDto> RunAsync(string workingDirectory, string targetPath, IReadOnlyList<string> arguments,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            Targets.Add(targetPath);
            return new DotnetCommandRunner().RunAsync(workingDirectory, targetPath, arguments, earlyKillPatterns, ct);
        }
    }

    [TestMethod]
    [DataRow(".sln")]
    [DataRow(".slnx")]
    public async Task RemovedMembershipUndo_UsesCurrentUnownedProjectBytes(string extension)
    {
        var source = await CreateMembershipFixtureAsync(extension, recordRestoreCoordinates: false);
        Exception? primary = null;
        try
        {
            var originalBytes = await File.ReadAllBytesAsync(source.SolutionPath);
            var original = WorkspaceManager.GetCurrentSolution(source.WorkspaceId);
            var projectB = original.Projects.Single(project => project.Name == "B");
            var projectPath = projectB.FilePath!;
            var documentPath = projectB.Documents.Single(document => Path.GetFileName(document.FilePath) == "B.cs").FilePath!;
            var modified = original.RemoveProject(projectB.Id);
            var changes = await SolutionDiffHelper.ComputeChangesAsync(original, modified,
                SolutionDiffReadContext.ForWorkspace(WorkspaceManager, source.WorkspaceId), CancellationToken.None);
            Assert.HasCount(1, changes.Where(change => FileSystemPath.Comparer.Equals(change.FilePath, source.SolutionPath)).ToArray());
            var token = PreviewStore.Store(source.WorkspaceId, original, modified,
                WorkspaceManager.GetCurrentVersion(source.WorkspaceId), "remove member B", changes, PreviewKind.Unspecified);
            var applied = await RefactoringService.ApplyRefactoringAsync(token, "test_apply", CancellationToken.None);
            Assert.IsTrue(applied.Success, applied.Error);
            Assert.HasCount(1, WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.ToArray());

            // B is no longer loaded. These later independent edits must remain current
            // when the owned inverse restores only its solution-membership write.
            await File.WriteAllTextAsync(documentPath, "public class B { public const int Current = 7; }\n", Encoding.UTF8);
            await File.WriteAllTextAsync(projectPath, (await File.ReadAllTextAsync(projectPath))
                .Replace("<Nullable>disable</Nullable>", "<Nullable>enable</Nullable>", StringComparison.Ordinal));
            var currentProjectBytes = await File.ReadAllBytesAsync(projectPath);
            var currentDocumentBytes = await File.ReadAllBytesAsync(documentPath);
            var beforeUndo = WorkspaceManager.GetCurrentVersion(source.WorkspaceId);
            Assert.IsTrue(await UndoService.RevertAsync(source.WorkspaceId));
            Assert.AreEqual(beforeUndo + 1, WorkspaceManager.GetCurrentVersion(source.WorkspaceId));
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(source.SolutionPath));
            CollectionAssert.AreEqual(currentProjectBytes, await File.ReadAllBytesAsync(projectPath));
            CollectionAssert.AreEqual(currentDocumentBytes, await File.ReadAllBytesAsync(documentPath));
            var restored = WorkspaceManager.GetCurrentSolution(source.WorkspaceId).Projects.Single(project => project.Name == "B");
            StringAssert.Contains((await restored.Documents.Single(document => document.FilePath == documentPath).GetTextAsync()).ToString(), "Current = 7");
            Assert.AreEqual(Microsoft.CodeAnalysis.NullableContextOptions.Enable,
                ((Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)restored.CompilationOptions!).NullableContextOptions);
            Assert.IsNull(UndoService.GetLastOperation(source.WorkspaceId));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Membership fixture cleanup failed.", primary,
                () => source.DisposeAsync());
        }
    }

    private static Task<IsolatedWorkspaceScope> CreateMembershipFixtureAsync(string extension, bool recordRestoreCoordinates)
    {
        var root = Path.Combine(TestTempRoot.Current, "membership-" + Guid.NewGuid().ToString("N"));
        var solutionPath = Path.Combine(root, "Membership" + extension);
        var scope = CreateWorkspaceScope(root, solutionPath, WorkspaceManager);
        return InitializeWithCleanupAsync(scope, async (fixture, ct) =>
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />", ct);
            foreach (var name in new[] { "A", "B" })
            {
                var directory = Path.Combine(root, name);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>disable</Nullable></PropertyGroup>"
                    + (recordRestoreCoordinates ? "<Target Name=\"RecordSolutionCoordinate\" BeforeTargets=\"Restore\"><WriteLinesToFile File=\"$(SolutionDir)fork-protocol.txt\" Lines=\"isolated\" Overwrite=\"true\" /></Target>" : "")
                    + "</Project>", ct);
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".cs"), "public class " + name + " { }\n", ct);
            }
            var solution = extension == ".slnx" ? "<Solution><Project Path=\"A/A.csproj\" /><Project Path=\"B/B.csproj\" /></Solution>"
                : "Microsoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version 17\n"
                    + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n"
                    + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"B\\B.csproj\", \"{22222222-2222-2222-2222-222222222222}\"\nEndProject\nGlobal\n"
                    + "GlobalSection(SolutionConfigurationPlatforms) = preSolution\nDebug|Any CPU = Debug|Any CPU\nEndGlobalSection\n"
                    + "GlobalSection(ProjectConfigurationPlatforms) = postSolution\n"
                    + "{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU\n"
                    + "{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\n{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU\nEndGlobalSection\nEndGlobal\n";
            await File.WriteAllTextAsync(solutionPath, solution, ct);
            await RestoreWorkspaceAsync(fixture, ct);
            await fixture.LoadAsync(ct);
        });
    }
}
