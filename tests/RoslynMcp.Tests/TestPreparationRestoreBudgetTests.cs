using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public sealed partial class TestRunnerEvaluationPropertiesTests
{
    [TestMethod]
    public async Task PreparationRestore_SelectedRootsCannotRenewSpentPhaseBudget()
    {
        var workspace = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(Path.Combine(_fixtureRoot, "Selected.slnx"), new Dictionary<string, string>()));
        Exception? resourceCleanupFailure1 = null;
        try
        {
            var (status, solution) = await PreparationGraphAsync(workspace, 2, false);
            var clock = new FakeTimeProvider();
            var runner = new RecordingRunner
            {
                RestoreSucceeds = true,
                RestoreSideEffect = () => clock.Advance(TimeSpan.FromMinutes(2))
            };
            var manager = NativeManager(status, solution);
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure2 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery(),
                    new ValidationServiceOptions { BuildTimeout = TimeSpan.FromMinutes(1) }, null,
                    owner => owner.Delete(CancellationToken.None), clock);

                await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.PrepareTestRunAsync("ws", null, null, TestContext.CancellationToken));
                Assert.HasCount(1, runner.Commands, "A later selected root must never renew a spent phase budget.");
                Assert.AreEqual("restore", runner.Commands[0][0]);

            }
            catch (Exception failure) { resourceCleanupFailure2 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure2, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure1 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure1, workspace); }
    }

    [TestMethod]
    public async Task PreparationRestore_SharedDependencyHasOneProducerAcrossSelectedRoots()
    {
        var workspace = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(Path.Combine(_fixtureRoot, "Selected.slnx"), new Dictionary<string, string>()));
        Exception? resourceCleanupFailure3 = null;
        try
        {
            var (status, solution) = await PreparationGraphAsync(workspace, 2, true);
            var runner = new RecordingRunner { RestoreSucceeds = true };
            var manager = NativeManager(status, solution);
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure4 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(FixturePolicy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
                await service.PrepareTestRunAsync("ws", null, null, TestContext.CancellationToken);

                var targets = runner.Commands.Select(command =>
                {
                    Assert.AreEqual("restore", command[0]);
                    return Path.GetFullPath(command[1]);
                }).ToArray();
                var dependency = Path.Combine(_fixtureRoot, "Common", "Common.csproj");
                Assert.HasCount(3, targets);
                Assert.HasCount(1, targets.Where(target => FileSystemPath.Comparer.Equals(target, dependency)));
                Assert.AreEqual(dependency, targets[0], "The shared dependency must precede both test roots.");
                CollectionAssert.AreEquivalent(status.Projects.Select(project => project.FilePath).Append(dependency).ToArray(), targets);

            }
            catch (Exception failure) { resourceCleanupFailure4 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure4, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure3 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure3, workspace); }
    }

    [TestMethod]
    public async Task PreparationRestore_FinalPostProducerObservationSharesPhaseCancellation()
    {
        var workspace = MSBuildWorkspace.Create(OwnedNativeLoadContext.Properties(Path.Combine(_fixtureRoot, "Selected.slnx"), new Dictionary<string, string>()));
        Exception? resourceCleanupFailure5 = null;
        try
        {
            var (status, solution) = await PreparationGraphAsync(workspace, 1, false);
            var clock = new FakeTimeProvider();
            var produced = false;
            var triggered = false;
            var runner = new RecordingRunner { RestoreSucceeds = true, RestoreSideEffect = () => produced = true };
            var ownedPolicy = FixturePolicy;
            var policy = new CallbackInputPolicy(async (id, paths, token) =>
            {
                await ownedPolicy.ValidateAsync(id, paths, token);
                if (produced && !triggered)
                {
                    triggered = true;
                    clock.Advance(TimeSpan.FromMinutes(2));
                    token.ThrowIfCancellationRequested();
                }
            });
            var manager = NativeManager(status, solution);
            var executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            Exception? resourceCleanupFailure6 = null;
            try
            {
                var service = new TestRunnerService(new ForkInvocationPathPolicy(policy), manager, executor,
                    NullLogger<TestRunnerService>.Instance, new UnusedDiscovery(),
                    new ValidationServiceOptions { BuildTimeout = TimeSpan.FromMinutes(1) }, null,
                    owner => owner.Delete(CancellationToken.None), clock);

                await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.PrepareTestRunAsync("ws", null, null, TestContext.CancellationToken));
                Assert.IsTrue(triggered, "The producer must finish before the post-producer grant expires.");
                Assert.HasCount(1, runner.Commands);

            }
            catch (Exception failure) { resourceCleanupFailure6 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure6, executor); }

        }
        catch (Exception failure) { resourceCleanupFailure5 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", resourceCleanupFailure5, workspace); }
    }

    private async Task<(WorkspaceStatusDto Status, Solution Solution)> PreparationGraphAsync(Microsoft.CodeAnalysis.Workspace workspace,
        int rootCount, bool sharedDependency)
    {
        var definitions = new List<string>();
        var common = Path.Combine(_fixtureRoot, "Common", "Common.csproj");
        if (sharedDependency)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(common)!);
            NativeSdkRestoreFixture.WriteProject(common, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        }
        for (var index = 0; index < rootCount; index++)
        {
            var name = "Root" + index;
            var path = Path.Combine(_fixtureRoot, name, name + ".csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var xml = new System.Xml.Linq.XElement("Project",
                new System.Xml.Linq.XElement("PropertyGroup",
                    new System.Xml.Linq.XElement("TargetFramework", "net10.0"),
                    new System.Xml.Linq.XElement("IsTestProject", "true")));
            if (sharedDependency)
                xml.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference",
                    new System.Xml.Linq.XAttribute("Include", Path.GetRelativePath(Path.GetDirectoryName(path)!, common)))));
            NativeSdkRestoreFixture.WriteProject(path, xml.ToString());
            definitions.Add(path);
        }
        var entry = Path.Combine(_fixtureRoot, "Selected.slnx");
        var slnx = new System.Xml.Linq.XElement("Solution", definitions.Select(path =>
            new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Path", Path.GetRelativePath(_fixtureRoot, path)))));
        await File.WriteAllTextAsync(entry, slnx.ToString(), TestContext.CancellationToken);
        var feed = Path.Combine(_fixtureRoot, "empty-feed");
        Directory.CreateDirectory(feed);
        var properties = EvaluatedProjectGraph.Properties(workspace.CurrentSolution);
        var restored = await new DotnetCommandRunner().RunAsync(_fixtureRoot, entry,
            MsBuildCommandProperties.Append(["restore", entry, "--nologo", "--source", feed, "--packages", Path.Combine(_fixtureRoot, "packages")], properties),
            TestContext.CancellationToken);
        Assert.IsTrue(restored.Succeeded, "Actual SDK/NuGet restore must establish the dependency/assets premise: " + restored.StdOut + restored.StdErr);
        _fixturePolicy = await NativeSdkRestoreFixture.CreatePolicyAsync(definitions[0], TestContext.CancellationToken);
        var graph = EvaluatedBuildGraph.FromRoots(definitions, properties,
            ImmutableDictionary<string, byte[]>.Empty, TestContext.CancellationToken);
        var selected = definitions.Select(path => graph.Nodes.Single(node => !node.IsOuterBuild && FileSystemPath.Comparer.Equals(node.ProjectPath, path))).ToArray();
        Assert.HasCount(rootCount + (sharedDependency ? 1 : 0), graph.Nodes.Where(node => !node.IsOuterBuild));
        var projects = selected.Select(node => new ProjectStatusDto(Path.GetFileNameWithoutExtension(node.ProjectPath), node.ProjectPath,
            0, [], ["net10.0"], true, Path.GetFileNameWithoutExtension(node.ProjectPath), "Library")).ToArray();
        var status = Status() with { LoadedPath = entry, ProjectCount = projects.Length, Projects = projects };
        var solution = workspace.CurrentSolution;
        foreach (var project in projects)
        {
            var node = selected.Single(node => FileSystemPath.Comparer.Equals(node.ProjectPath, project.FilePath));
            solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), project.Name, project.Name,
                LanguageNames.CSharp, filePath: project.FilePath, outputFilePath: node.TargetPath));
        }
        return (status, solution);
    }
}
