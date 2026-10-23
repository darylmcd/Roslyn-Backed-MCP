using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

public sealed partial class TestRunnerEvaluationPropertiesTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestStart_FinalConfigurationGrantCannotChangeHiddenCompilerInput(bool includedRuleset)
    {
        var ct = TestContext.CancellationToken;
        NuGetInstallationFixture? fixture = null;
        WorkspaceManager? manager = null;
        IGatedCommandExecutor? executor = null;
        Exception? primary = null;
        try
        {
            fixture = await NuGetInstallationFixture.CreateAsync("Owned.TestStartClosure", "1.0.0",
                new Dictionary<string, byte[]> { ["marker.txt"] = "owned dependency"u8.ToArray() });
            var project = Path.Combine(fixture.Root, "Owner.csproj");
            var source = Path.Combine(fixture.Root, "Test.cs");
            var hidden = Path.Combine(fixture.Root, includedRuleset ? "included.ruleset" : "nested.rsp");
            await File.WriteAllTextAsync(source, "public class Test { }", ct);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "outer.rsp"),
                includedRuleset ? "/ruleset:root.ruleset\n" : "@nested.rsp\n", ct);
            if (includedRuleset)
            {
                await File.WriteAllTextAsync(Path.Combine(fixture.Root, "root.ruleset"),
                    """<RuleSet Name="Root" ToolsVersion="15.0"><Include Path="included.ruleset" Action="Default" /></RuleSet>""", ct);
                await File.WriteAllTextAsync(hidden, """<RuleSet Name="Included" ToolsVersion="15.0" />""", ct);
            }
            else await File.WriteAllTextAsync(hidden, "/define:INITIAL\n", ct);
            var definition = System.Xml.Linq.XDocument.Load(project);
            definition.Root!.Add(new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XElement("IsTestProject", "true"),
                new System.Xml.Linq.XElement("CompilerResponseFile", "outer.rsp")));
            definition.Root.Add(new System.Xml.Linq.XElement("ItemGroup",
                new System.Xml.Linq.XElement("None", new System.Xml.Linq.XAttribute("Remove", "*.rsp;*.ruleset"))));
            await File.WriteAllTextAsync(project, definition.ToString(), ct);
            var global = Path.Combine(fixture.Root, "global.json");
            var policy = new TestStartClosingPolicy(project, source, hidden, global, async token =>
                await File.AppendAllTextAsync(hidden, includedRuleset
                    ? "<!-- changed at final test configuration grant -->"
                    : "# changed at final test configuration grant\n", token));
            var scopedPolicy = new ForkInvocationPathPolicy(policy);
            manager = new WorkspaceManager(scopedPolicy, NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(), new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 2 });
            WorkspaceStatusDto status = await manager.LoadAsync(project, fixture.LoadProperties, ct);
            Solution solution = manager.GetCurrentSolution(status.WorkspaceId);
            var certificate = NativeCompilerContextCertificate.Require(solution);
            CollectionAssert.Contains(certificate.FinalRestoreGrantPaths(solution).ToArray(), hidden);
            var native = EvaluatedBuildGraph.Source(solution, CancellationToken.None);
            Assert.IsFalse(native.Nodes.SelectMany(node => node.DocumentFiles.Concat(node.ArtifactFiles)
                .Concat(node.PathEvidence.InputFiles)).Contains(hidden, FileSystemPath.Comparer),
                "The hidden parser input must require its retained receipt.");
            var runner = new RecordingRunner { RestoreSucceeds = true };
            executor = new QueuedTestAdmissionExecutor(new GatedCommandExecutor(manager, runner, NullLogger<GatedCommandExecutor>.Instance), policy);
            var service = new TestRunnerService(scopedPolicy, manager, executor,
                NullLogger<TestRunnerService>.Instance, new UnusedDiscovery());
            TestRunPlan plan = await service.PrepareTestRunAsync(status.WorkspaceId, null, null, ct);
            Assert.IsTrue(policy.ClosingBatchObserved, "Preparation must exercise the real complete certificate grant.");
            Assert.IsEmpty(runner.Commands);
            policy.Arm();
            Exception? refused = null;
            try { await service.RunTestCommandAsync(plan, ct); }
            catch (Exception failure) { refused = failure; }
            Assert.IsTrue(policy.QueuedAdmissionEntered, "The real executor must enter its queued before-start authorization.");
            Assert.IsTrue(policy.ClosingBatchObserved, "Queued admission must finish a real certificate grant before the later configuration grant.");
            Assert.AreEqual(1, policy.Mutations);
            StringAssert.Contains(await File.ReadAllTextAsync(hidden, ct), "changed at final test configuration grant");
            Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType(),
                "The actual later configuration grant must not invalidate an earlier compiler proof and still launch.");
            Assert.IsEmpty(runner.Commands, "No command may launch after a hidden compiler input changes.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Test-start closure fixture cleanup failed.", primary, executor, manager, fixture);
        }
    }

    private sealed class TestStartClosingPolicy(string project, string source, string hidden, string global,
        Func<CancellationToken, Task> mutate) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        private bool _armed;
        private readonly AsyncLocal<bool> _queuedAdmission = new();
        internal bool QueuedAdmissionEntered { get; private set; }
        internal async Task AuthorizeQueuedAsync(Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
        {
            var previous = _queuedAdmission.Value;
            _queuedAdmission.Value = true;
            QueuedAdmissionEntered = true;
            ClosingBatchObserved = false;
            try { await authorizeStart(ct); }
            finally { _queuedAdmission.Value = previous; }
        }
        internal bool ClosingBatchObserved { get; private set; }
        internal int Mutations { get; private set; }
        internal void Arm() { _armed = true; ClosingBatchObserved = false; }

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths, ct);
            if (paths.Contains(project, FileSystemPath.Comparer) && paths.Contains(source, FileSystemPath.Comparer)
                && paths.Contains(hidden, FileSystemPath.Comparer))
                ClosingBatchObserved = true;
            if (!_armed || !_queuedAdmission.Value || !ClosingBatchObserved || Mutations != 0 || paths.Count != 1
                || !FileSystemPath.Comparer.Equals(paths[0], global)) return;
            Mutations++;
            await mutate(ct);
        }
    }
    private sealed class QueuedTestAdmissionExecutor(IGatedCommandExecutor inner, TestStartClosingPolicy policy) : IGatedCommandExecutor
    {
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken ct) => inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);
        public Task<CommandExecutionDto> ExecuteAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? patterns, CancellationToken ct)
            => inner.ExecuteAsync(workspaceId, targetPath, arguments, timeout, patterns, ct);
        public Task<TestRunResultDto> ExecuteTestAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? patterns, Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
            => inner.ExecuteTestAsync(workspaceId, targetPath, arguments, collectResults, timeout, patterns,
                token => policy.AuthorizeQueuedAsync(authorizeStart, token), ct);
        public Task<CommandExecutionDto> ExecuteAuthorizedAsync(string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            TimeSpan timeout, Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
            => inner.ExecuteAuthorizedAsync(workspaceId, targetPath, arguments, timeout, authorizeStart, ct);
        public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct)
            => inner.ExecuteBuildAsync(plan, timeout, ct);
        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) => inner.ResolveProject(workspaceId, projectName);
        public void Dispose() => inner.Dispose();
    }

}
