using System.Collections.Immutable;
using System.Xml.Linq;
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
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PreparationRestore_InitialDiscoverySharesTheOriginalPhaseDeadline(bool cancelCaller, bool callbackTimesOut)
    {
        NuGetInstallationFixture? fixture = null;
        MSBuildWorkspace? workspace = null;
        GatedCommandExecutor? executor = null;
        CancellationTokenSource? caller = null;
        Exception? primary = null;
        try
        {
            caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            fixture = await NuGetInstallationFixture.CreateAsync("TUnit", "1.0.0",
                new Dictionary<string, byte[]> { ["content/readme.txt"] = "owned offline protocol dependency"u8.ToArray() });
            var projectPath = Path.Combine(fixture.Root, "Owner.csproj");
            var definition = XDocument.Load(projectPath);
            definition.Root!.Add(new XElement("PropertyGroup", new XElement("IsTestProject", "true"),
                new XElement("OutputType", "Exe")));
            await File.WriteAllTextAsync(projectPath, definition.ToString(), caller.Token);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"),
                """{"test":{"runner":"Microsoft.Testing.Platform"}}""", caller.Token);
            var properties = fixture.LoadProperties;
            properties["RestorePackagesPath"] = fixture.Cache;
            var restored = await new DotnetCommandRunner().RunAsync(fixture.Root, projectPath,
                MsBuildCommandProperties.Append(["restore", projectPath, "--nologo", "--source",
                    Path.Combine(fixture.Root, "feed"), "--packages", fixture.Cache], properties), caller.Token);
            Assert.IsTrue(restored.Succeeded, "The selecting assets must come from an actual offline NuGet restore.");
            Assert.IsTrue(File.Exists(fixture.Assets));
            var graph = EvaluatedBuildGraph.FromRoots([projectPath], properties, ImmutableDictionary<string, byte[]>.Empty, caller.Token);
            var native = graph.Nodes.Single(node => !node.IsOuterBuild);
            Assert.IsTrue(native.Inputs.IsTestProject);
            Assert.AreEqual("Exe", native.Inputs.OutputType);
            Assert.IsTrue(native.Inputs.Packages.Any(package => package.Id == "TUnit"));
            workspace = MSBuildWorkspace.Create(properties);
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(),
                "Owner", "Owner", LanguageNames.CSharp, filePath: projectPath, outputFilePath: native.TargetPath));
            var project = solution.GetProject(projectId)!;
            Assert.IsFalse(NativeCompilerContextCertificate.TryGet(project.Solution, out _),
                "This tests the actual certificate-null restore route, never an invented or downgraded certified receipt.");
            var status = new WorkspaceStatusDto("initial-discovery", projectPath, 1, "snapshot", DateTimeOffset.UtcNow,
                1, 0, [new("Owner", projectPath, 0, [], ["net10.0"], true, "Owner", "Exe")], true, false, []);
            var manager = new FailClosedWorkspaceManagerStub(status)
            {
                GetStatusHandler = _ => status,
                GetStatusAsyncHandler = _ => status,
                GetCurrentSolutionHandler = _ => project.Solution,
                GetCurrentVersionHandler = _ => 1
            };
            var policy = new ForkInvocationPathPolicy(await NativeSdkRestoreFixture.CreatePolicyAsync(projectPath, caller.Token));
            var clock = new FakeTimeProvider();
            var triggered = false;
            var callbackCanceledAtDeadline = false;
            var discovery = new InitialBudgetDiscovery((id, token) =>
            {
                Assert.AreEqual(status.WorkspaceId, id);
                Assert.IsFalse(triggered, "Only the actual initial protocol discovery is expected.");
                triggered = true;
                clock.Advance(TimeSpan.FromMinutes(2));
                callbackCanceledAtDeadline = token.IsCancellationRequested;
                if (cancelCaller) caller.Cancel();
                if (callbackTimesOut) throw new TimeoutException("The actual discovery callback reported its timeout after caller cancellation.");
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new TestDiscoveryDto([new("Owner", projectPath,
                    [new("Case", "Owned.Tests.Case", null, null)])]));
            });
            var runner = new RecordingRunner { RestoreSucceeds = true };
            executor = new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance);
            var service = new TestRunnerService(policy, manager, executor, NullLogger<TestRunnerService>.Instance, discovery,
                new ValidationServiceOptions { BuildTimeout = TimeSpan.FromMinutes(1) }, null,
                owner => owner.Delete(CancellationToken.None), clock);
            Exception? observed = null;
            try { await service.PrepareTestRunAsync(status.WorkspaceId, "Owner", "FullyQualifiedName=Owned.Tests.Case", caller.Token); }
            catch (Exception failure) { observed = failure; }
            Assert.IsTrue(triggered, "The actor must execute inside actual filtered MTP protocol discovery. Observed="
                + observed?.GetType().Name + ": " + observed?.Message);
            Assert.IsTrue(callbackCanceledAtDeadline, "The discovery callback must carry the spent restore deadline before caller cancellation.");
            Assert.HasCount(0, runner.Commands, "Expired initial protocol work cannot reach any restore or test producer.");
            if (cancelCaller)
            {
                Assert.IsInstanceOfType<OperationCanceledException>(observed);
                Assert.AreEqual(caller.Token, ((OperationCanceledException)observed!).CancellationToken);
            }
            else Assert.IsInstanceOfType<TimeoutException>(observed);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Initial restore discovery fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => executor?.Dispose()),
                CleanupFailureCollector.FromAction(() => workspace?.Dispose()),
                CleanupFailureCollector.FromAction(() => fixture?.Dispose()),
                CleanupFailureCollector.FromAction(() => caller?.Dispose()));
        }
    }

    private sealed class InitialBudgetDiscovery(Func<string, CancellationToken, Task<TestDiscoveryDto>> discover) : ITestDiscoveryService
    {
        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) => discover(workspaceId, ct);
        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct) => throw new NotSupportedException();
        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId, IReadOnlyList<string> paths, int maxResults, CancellationToken ct) => throw new NotSupportedException();
    }
}
