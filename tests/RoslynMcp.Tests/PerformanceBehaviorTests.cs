using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// donotparallelize-audit-wave-18: opt-out removed. Workspace_Status_Uses_Loaded_Metadata_Until_Reload loads,
// edits, reloads and closes its own temp copy of SampleSolution (CreateSampleSolutionCopy) under its own
// workspace id, the same private-copy WorkspaceManager.LoadAsync pattern the parallel classes
// MoveTypeDiskStateTests and CouplingAnalysisTests use; the other two methods run against local
// configured workspace stub / BlockingDotnetCommandRunner instances. No method touches the
// WorkspaceIdCache-shared workspace, a static, or the environment. Native child processes
// belong to the private workspace fixture; the other two methods use local runners.
[TestClass]
public class PerformanceBehaviorTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [TestMethod]
    public async Task Workspace_Status_Uses_Loaded_Metadata_Until_Reload()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var appProjectPath = Path.Combine(copiedRoot, "SampleApp", "SampleApp.csproj");
        string? workspaceId = null;
        Exception? primary = null;

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(copiedSolutionPath, new Dictionary<string, string>()), CancellationToken.None);
            workspaceId = status.WorkspaceId;

            var initialStatus = WorkspaceManager.GetStatus(workspaceId);
            var initialProject = initialStatus.Projects.First(project => project.Name == "SampleApp");
            Assert.AreEqual("SampleApp", initialProject.AssemblyName);

            var originalProjectText = await File.ReadAllTextAsync(appProjectPath, CancellationToken.None);
            var updatedProjectText = originalProjectText.Replace(
                "<ImplicitUsings>enable</ImplicitUsings>",
                "<ImplicitUsings>enable</ImplicitUsings>\r\n    <AssemblyName>SampleApp.Reloaded</AssemblyName>");
            await File.WriteAllTextAsync(appProjectPath, updatedProjectText, CancellationToken.None);

            var staleStatus = WorkspaceManager.GetStatus(workspaceId);
            Assert.AreEqual(
                "SampleApp",
                staleStatus.Projects.First(project => project.Name == "SampleApp").AssemblyName,
                "Status should describe the loaded workspace snapshot until reload.");

            await WorkspaceManager.ReloadAsync(workspaceId, CancellationToken.None);

            var reloadedStatus = WorkspaceManager.GetStatus(workspaceId);
            Assert.AreEqual(
                "SampleApp.Reloaded",
                reloadedStatus.Projects.First(project => project.Name == "SampleApp").AssemblyName);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Workspace status fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (workspaceId is not null) WorkspaceManager.Close(workspaceId); }),
                CleanupFailureCollector.FromAction(() => DeleteDirectoryIfExists(copiedRoot)));
        }
    }

    [TestMethod]
    public async Task RW_Lock_Allows_Parallel_Reads_For_Same_Workspace()
    {
        // Two parallel RunReadAsync calls against the same workspace should overlap on the
        // per-workspace AsyncReaderWriterLock.
        var manager = CreateWorkspaceManager();
        WorkspaceExecutionGate? gate = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tracker = new ConcurrencyTracker();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? first = null;
        Task<int>? second = null;
        Exception? primary = null;
        try
        {
            gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
            first = gate.RunReadAsync("workspace-a", async ct =>
            {
                tracker.Enter();
                try
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(ct);
                    return 1;
                }
                finally { tracker.Leave(); }
            }, cts.Token);
            await firstStarted.Task.WaitAsync(cts.Token);
            second = gate.RunReadAsync("workspace-a", _ =>
            {
                tracker.Enter();
                try
                {
                    secondStarted.SetResult();
                    return Task.FromResult(2);
                }
                finally { tracker.Leave(); }
            }, cts.Token);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
            Assert.AreEqual(2, tracker.MaxConcurrent,
                "Two reads on the same workspace must overlap on the per-workspace RW lock.");
            releaseFirst.TrySetResult();
            await Task.WhenAll(first, second);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            releaseFirst.TrySetResult();
            await CleanupFailureCollector.RunAfterFailureAsync("Parallel reader fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (first is { IsCompleted: false } || second is { IsCompleted: false }) cts.Cancel(); }),
                async () => { if (first is not null) await first; },
                async () => { if (second is not null) await second; },
                CleanupFailureCollector.FromAction(() => gate?.Dispose()));
        }
    }

    [TestMethod]
    public async Task Validation_Service_Serializes_Commands_For_The_Same_Workspace()
    {
        var workspaceManager = CreateWorkspaceManager();
        var commandRunner = new BlockingDotnetCommandRunner();
        GatedCommandExecutor? executor = null;
        CompilationCache? cache = null;
        Task? firstTask = null;
        Task? secondTask = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Exception? primary = null;
        try
        {
            executor = new GatedCommandExecutor(workspaceManager, commandRunner, NullLogger<GatedCommandExecutor>.Instance);
            cache = new CompilationCache(workspaceManager);
            var buildService = new BuildService(workspaceManager, executor, cache, NullLogger<BuildService>.Instance);
            firstTask = buildService.BuildWorkspaceAsync("workspace-a", cts.Token);
            await commandRunner.FirstInvocationStarted.Task.WaitAsync(cts.Token);
            secondTask = buildService.BuildWorkspaceAsync("workspace-a", cts.Token);
            Assert.IsFalse(secondTask.IsCompleted,
                "Second build should be waiting on the workspace command gate while the first build is blocked.");
            Assert.AreEqual(1, commandRunner.MaxConcurrentCalls);
            commandRunner.Release();
            await Task.WhenAll(firstTask, secondTask);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            commandRunner.Release();
            await CleanupFailureCollector.RunAfterFailureAsync("Serialized command fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (firstTask is { IsCompleted: false } || secondTask is { IsCompleted: false }) cts.Cancel(); }),
                async () => { if (firstTask is not null) await firstTask; },
                async () => { if (secondTask is not null) await secondTask; },
                CleanupFailureCollector.FromAction(() => cache?.Dispose()),
                CleanupFailureCollector.FromAction(() => executor?.Dispose()));
        }
    }

    private static FailClosedWorkspaceManagerStub CreateWorkspaceManager()
    {
        static WorkspaceStatusDto Status(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: @"C:\repo\Sample.slnx",
                WorkspaceVersion: 1,
                SnapshotToken: $"{workspaceId}:1",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: 1,
                DocumentCount: 1,
                Projects:
                [
                    new ProjectStatusDto(
                        Name: "Sample",
                        FilePath: @"C:\repo\Sample\Sample.csproj",
                        DocumentCount: 1,
                        ProjectReferences: [],
                        TargetFrameworks: ["net10.0"],
                        IsTestProject: false,
                        AssemblyName: "Sample",
                        OutputType: "Library")
                ],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []);
        return new FailClosedWorkspaceManagerStub
        {
            ContainsWorkspaceHandler = workspaceId => !string.IsNullOrWhiteSpace(workspaceId),
            IsStaleHandler = _ => false,
            GetStatusHandler = Status,
            GetStatusAsyncHandler = Status,
            GetCurrentVersionHandler = _ => 1,
        };
    }

    private sealed class BlockingDotnetCommandRunner : RoslynMcp.Tests.Helpers.CannedCommandRunner
    {
        private readonly TaskCompletionSource<bool> _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrencyTracker _concurrency = new();

        public TaskCompletionSource<bool> FirstInvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentCalls => _concurrency.MaxConcurrent;

        public override async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            _concurrency.Enter();
            FirstInvocationStarted.TrySetResult(true);

            try
            {
                await _releaseTcs.Task.WaitAsync(ct);
            }
            finally
            {
                _concurrency.Leave();
            }
            return new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: 0,
                Succeeded: true,
                DurationMs: 1,
                StdOut: "",
                StdErr: "");
        }

        public void Release()
        {
            _releaseTcs.TrySetResult(true);
        }
    }
}
