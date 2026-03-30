using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Tests;

[TestClass]
public class PerformanceBehaviorTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        InitializeServices();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        DisposeServices();
    }

    [TestMethod]
    public async Task Workspace_Status_Uses_Loaded_Metadata_Until_Reload()
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        var appProjectPath = Path.Combine(copiedRoot, "SampleApp", "SampleApp.csproj");

        try
        {
            var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
            var workspaceId = status.WorkspaceId;

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
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    [TestMethod]
    public async Task Validation_Service_Serializes_Commands_For_The_Same_Workspace()
    {
        var workspaceManager = new FakeWorkspaceManager();
        var commandRunner = new BlockingDotnetCommandRunner();
        var executor = new GatedCommandExecutor(
            workspaceManager,
            commandRunner,
            NullLogger<GatedCommandExecutor>.Instance);
        var buildService = new BuildService(
            workspaceManager,
            executor,
            NullLogger<BuildService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var firstTask = buildService.BuildWorkspaceAsync("workspace-a", cts.Token);
        await commandRunner.FirstInvocationStarted.Task;

        var secondTask = buildService.BuildWorkspaceAsync("workspace-a", cts.Token);
        await Task.Delay(150, cts.Token);

        Assert.AreEqual(1, commandRunner.MaxConcurrentCalls);

        commandRunner.Release();
        await Task.WhenAll(firstTask, secondTask);
    }

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();

        public WorkspaceStatusDto GetStatus(string workspaceId) =>
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

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool Close(string workspaceId) => true;

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public int GetCurrentVersion(string workspaceId) => 1;

        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) => throw new NotSupportedException();
    }

    private sealed class BlockingDotnetCommandRunner : IDotnetCommandRunner
    {
        private readonly TaskCompletionSource<bool> _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _currentCalls;

        public TaskCompletionSource<bool> FirstInvocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentCalls { get; private set; }

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            var concurrentCalls = Interlocked.Increment(ref _currentCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, concurrentCalls);
            FirstInvocationStarted.TrySetResult(true);

            await _releaseTcs.Task.WaitAsync(ct);

            Interlocked.Decrement(ref _currentCalls);
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
