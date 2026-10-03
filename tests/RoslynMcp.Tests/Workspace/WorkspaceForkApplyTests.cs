using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

// donotparallelize-audit-wave-35: [DoNotParallelize] removed. Each fork-apply test loads its own
// GUID-unique IsolatedWorkspaceScope copy, stores a token-keyed preview for that workspace id only,
// and copies/replays workspace_fork_apply under the per-workspace write gate. The fork is created under the
// copy's own .roslynmcp/forks directory, the static ForkApplyLocks entry is keyed by that unique
// source root, and the retained fork workspace is closed and deleted in finally. The fork's real
// `dotnet restore` targets only the fork directory — the same shape parallel-enabled
// CrossProjectRefactoringIntegrationTests and ScaffoldingFirstTestFileTests already run
// concurrently. Validation receives explicit appliedFiles, so the shared ChangeTracker is not read.
// The remaining tests are pure (ShouldRetainFork) or run on per-test fake command runners. No
// shared-workspace reload or close, no static or environment mutation. Validated by a bounded
// repeated (3x) concurrent run alongside its wave-35 siblings and parallel-enabled
// workspace-loading classes, green every time.
[TestClass]
public sealed class WorkspaceForkApplyTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task WorkspaceForkApply_KeepSuccess_MutatesOnlyFork()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var dogPath = workspace.GetPath("SampleLib", "Dog.cs");
        var originalSource = await File.ReadAllTextAsync(dogPath, CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            CreateForkApplyService(validationService),
            workspace.WorkspaceId,
            token,
            retention: "keep",
            runTests: false,
            testFilter: null,
            forkName: "success",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean());

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained forks.");

        try
        {
            var forkDogPath = Path.Combine(forkPath, "SampleLib", "Dog.cs");
            StringAssert.Contains(await File.ReadAllTextAsync(forkDogPath, CancellationToken.None), "ForkWoof");
            Assert.AreEqual(originalSource, await File.ReadAllTextAsync(dogPath, CancellationToken.None),
                "workspace_fork_apply must not mutate the source workspace file.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_DefaultRetention_KeepsFailedForkLoadable()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync(CancellationToken.None);
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, _ => "namespace SampleLib; public class Dog {");
        var validationService = CreateValidationService();

        var json = await ValidationBundleTools.WorkspaceForkApply(
            CreateForkApplyService(validationService),
            workspace.WorkspaceId,
            token,
            retention: "drop-on-success",
            runTests: false,
            testFilter: null,
            forkName: "failure",
            ct: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.IsTrue(root.GetProperty("retained").GetBoolean(),
            "drop-on-success should retain failed forks for inspection.");

        var forkPath = root.GetProperty("forkPath").GetString()
            ?? throw new AssertFailedException("forkPath should be returned.");
        var forkWorkspaceId = root.GetProperty("forkWorkspaceId").GetString()
            ?? throw new AssertFailedException("forkWorkspaceId should be returned for retained failed forks.");

        try
        {
            Assert.IsTrue(Directory.Exists(forkPath), "Failed retained fork should remain on disk.");
            Assert.IsTrue(WorkspaceManager.ContainsWorkspace(forkWorkspaceId),
                "Retained failed fork should remain loaded for follow-up inspection.");
        }
        finally
        {
            WorkspaceManager.Close(forkWorkspaceId);
            DeleteDirectoryIfExists(forkPath);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WorkspaceForkApply_TestsDoNotBlockSourceWrite(bool explicitFilter)
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingTestRunner(entered, release);
        var validation = new WorkspaceValidationService(
            CompileCheckService, DiagnosticService, new FixedDiscovery(), runner, WorkspaceManager);
        var service = new WorkspaceForkApplyService(
            WorkspaceManager, WorkspaceExecutionGate, PreviewStore, validation, runner, new DotnetCommandRunner(),
            NullLogger<WorkspaceForkApplyService>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var forkTask = ValidationBundleTools.WorkspaceForkApply(
            service, workspace.WorkspaceId, token,
            retention: "drop-always", runTests: true,
            testFilter: explicitFilter ? "FullyQualifiedName=ForkProbe" : null,
            ct: cancellation.Token);
        Task<bool>? writeTask = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            writeTask = WorkspaceExecutionGate.RunWriteAsync(workspace.WorkspaceId, async ct =>
            {
                await File.AppendAllTextAsync(workspace.GetPath("SampleLib", "Dog.cs"),
                    "\n// source write during fork tests\n", ct);
                return true;
            }, cancellation.Token, applyStalenessPolicy: false);
            Assert.IsTrue(await writeTask.WaitAsync(TimeSpan.FromSeconds(2)),
                "Source writes must complete while fork tests are still blocked.");
            Assert.IsFalse(forkTask.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await forkTask;
            if (writeTask is not null)
                await writeTask;
        }
    }

    [TestMethod]
    public async Task WorkspaceForkApply_CallerCancellation_ClosesLoadedFork()
    {
        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var token = await StoreDogPreviewAsync(workspace.WorkspaceId, source =>
            source.Replace("Woof", "ForkWoof", StringComparison.Ordinal));
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingTestRunner(cancellation);
        var service = new WorkspaceForkApplyService(
            WorkspaceManager, WorkspaceExecutionGate, PreviewStore, CreateValidationService(), runner,
            new DotnetCommandRunner(), NullLogger<WorkspaceForkApplyService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ValidationBundleTools.WorkspaceForkApply(
            service, workspace.WorkspaceId, token, runTests: true,
            testFilter: "FullyQualifiedName=ForkProbe", ct: cancellation.Token));
        Assert.IsNotNull(runner.WorkspaceId);
        Assert.IsFalse(WorkspaceManager.ContainsWorkspace(runner.WorkspaceId),
            "Cancellation after fork loading must close the fork workspace before deleting its files.");
        Assert.IsNotNull(runner.ForkPath);
        Assert.IsFalse(Directory.Exists(runner.ForkPath), "Cancelled fork files must also be removed.");
    }

    [TestMethod]
    [DataRow("drop-on-success", true, false)]
    [DataRow("drop-on-success", false, true)]
    [DataRow("drop-on-failure", true, true)]
    [DataRow("drop-on-failure", false, false)]
    [DataRow("drop-always", true, false)]
    [DataRow("drop-always", false, false)]
    [DataRow("keep", true, true)]
    [DataRow("keep", false, true)]
    public void ShouldRetainFork_AllPolicies_OwnTheirStateTransition(
        string retention,
        bool success,
        bool expected)
    {
        Assert.AreEqual(expected, WorkspaceForkApplyService.ShouldRetainFork(retention, success));
    }

    [TestMethod]
    public async Task RestoreForkAsync_Failure_RedactsCredentialBearingOutput()
    {
        const string secret = "super-secret-feed-password";
        var runner = new FailingRestoreRunner(
            $"NU1301: Unable to load https://build:{secret}@feed.example/v3/index.json" +
            $"?api_key={secret} Authorization: Bearer {secret}");
        var service = new WorkspaceForkApplyService(
            workspaceManager: null!,
            workspaceExecutionGate: null!,
            previewStore: null!,
            validationService: null!,
            testRunnerService: null!,
            commandRunner: runner,
            logger: NullLogger<WorkspaceForkApplyService>.Instance);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.RestoreForkAsync(
                Path.Combine(Path.GetTempPath(), "fork", "SampleSolution.slnx"),
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "exit code 1");
        StringAssert.Contains(exception.Message, "[redacted]");
        Assert.IsFalse(
            exception.Message.Contains(secret, StringComparison.Ordinal),
            "Client-facing restore failures must not expose credential-shaped output.");
        Assert.IsTrue(
            exception.Message.Length < 700,
            "Client-facing restore failures must stay bounded.");
    }

    [TestMethod]
    public async Task RestoreForkAsync_NonTimeoutCancellation_PropagatesWithoutTimeoutReclassification()
    {
        var expected = new OperationCanceledException("runner-owned cancellation");
        var service = new WorkspaceForkApplyService(
            workspaceManager: null!,
            workspaceExecutionGate: null!,
            previewStore: null!,
            validationService: null!,
            testRunnerService: null!,
            commandRunner: new CancelingRestoreRunner(expected),
            logger: NullLogger<WorkspaceForkApplyService>.Instance);

        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.RestoreForkAsync(
                Path.Combine(Path.GetTempPath(), "fork", "SampleSolution.slnx"),
                CancellationToken.None));

        Assert.AreSame(expected, actual,
            "Only the service-owned timeout token may be reclassified as TimeoutException.");
    }

    private static WorkspaceValidationService CreateValidationService() =>
        new(
            CompileCheckService,
            DiagnosticService,
            TestDiscoveryService,
            TestRunnerService,
            WorkspaceManager,
            ChangeTracker);

    private static WorkspaceForkApplyService CreateForkApplyService(
        WorkspaceValidationService validationService) =>
        new(
            WorkspaceManager,
            WorkspaceExecutionGate,
            PreviewStore,
            validationService,
            TestRunnerService,
            new DotnetCommandRunner(),
            NullLogger<WorkspaceForkApplyService>.Instance);

    private static async Task<string> StoreDogPreviewAsync(string workspaceId, Func<string, string> mutate)
    {
        var solution = WorkspaceManager.GetCurrentSolution(workspaceId);
        var dog = solution.Projects
            .SelectMany(project => project.Documents)
            .First(document => string.Equals(document.Name, "Dog.cs", StringComparison.Ordinal));

        var source = (await dog.GetTextAsync(CancellationToken.None).ConfigureAwait(false)).ToString();
        var modified = solution.WithDocumentText(dog.Id, SourceText.From(mutate(source)));

        return PreviewStore.Store(
            workspaceId,
            modified,
            WorkspaceManager.GetCurrentVersion(workspaceId),
            "workspace_fork_apply test preview");
    }

    private sealed class FailingRestoreRunner(string stderr) : IDotnetCommandRunner
    {
        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: 1,
                Succeeded: false,
                DurationMs: 1,
                StdOut: string.Empty,
                StdErr: stderr));
    }

    private sealed class CancelingRestoreRunner(OperationCanceledException exception) : IDotnetCommandRunner
    {
        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromException<CommandExecutionDto>(exception);
    }

    private sealed class BlockingTestRunner(TaskCompletionSource entered, TaskCompletionSource release) : ITestRunnerService
    {
        public async Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new TestRunResultDto(
                new CommandExecutionDto("dotnet", ["test"], string.Empty, string.Empty, 0, true, 0, string.Empty, string.Empty),
                Total: 1, Passed: 1, Failed: 0, Skipped: 0, Failures: []);
        }
    }

    private sealed class FixedDiscovery : ITestDiscoveryService
    {
        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
            string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            Task.FromResult(new RelatedTestsForFilesDto([], "FullyQualifiedName=ForkProbe",
                new PaginationInfo(0, 0, false), new RelatedTestsDiagnosticsDto(1, ["fixed test fixture"], [])));

        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator,
            int maxResults, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CancellingTestRunner(CancellationTokenSource cancellation) : ITestRunnerService
    {
        public string? WorkspaceId { get; private set; }
        public string? ForkPath { get; private set; }

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId, string? projectName, string? filter, CancellationToken ct)
        {
            WorkspaceId = workspaceId;
            ForkPath = Path.GetDirectoryName(WorkspaceManager.GetStatus(workspaceId).LoadedPath);
            cancellation.Cancel();
            return Task.FromCanceled<TestRunResultDto>(ct);
        }
    }
}
