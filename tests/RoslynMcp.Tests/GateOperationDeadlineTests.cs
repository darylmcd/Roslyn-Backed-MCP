using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// build_workspace / build_project must not hold the workspace gate across the dotnet build:
/// the gate's 2-minute request timeout (armed before the lock wait) would otherwise beat the
/// 5-minute <c>BuildTimeout</c> and pin a throttle slot plus the reader lock for the whole command.
/// </summary>
[TestClass]
public sealed class GateOperationDeadlineTests
{
    private const string WorkspaceId = "ws-build";

    private sealed class Harness
    {
        public FakeTimeProvider Clock { get; } = new();
        public BuildWorkspaceManager Manager { get; } = new();
        public ControlledCommandRunner Runner { get; } = new();
        public WorkspaceExecutionGate Gate { get; }
        public BuildService Service { get; }

        public Harness(TimeSpan? buildTimeout = null)
        {
            Gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), Manager, Clock);
            var executor = new GatedCommandExecutor(Manager, Runner, NullLogger<GatedCommandExecutor>.Instance);
            var options = new ValidationServiceOptions { BuildTimeout = buildTimeout ?? TimeSpan.FromMinutes(5) };
            Service = new BuildService(
                Manager, executor, new CompilationCache(Manager), NullLogger<BuildService>.Instance, options);
        }

        public Task<string> BuildWorkspaceAsync() =>
            ValidationTools.BuildWorkspace(Gate, Service, WorkspaceId, progress: null, CancellationToken.None);

        public Task<string> BuildProjectAsync() =>
            ValidationTools.BuildProject(Gate, Service, WorkspaceId, "Sample", CancellationToken.None);
    }

    [TestMethod]
    public async Task BuildWorkspace_RunsPastGateRequestTimeout_WhenBuildIsStillWithinBuildTimeout()
    {
        var h = new Harness();

        var build = h.BuildWorkspaceAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Gate clock passes the 2-minute request timeout while the build command is blocked.
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Runner.Release();

        var json = await build.WaitAsync(TimeSpan.FromSeconds(10));
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public async Task BuildProject_RunsPastGateRequestTimeout_WhenBuildIsStillWithinBuildTimeout()
    {
        var h = new Harness();

        var build = h.BuildProjectAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Runner.Release();

        var json = await build.WaitAsync(TimeSpan.FromSeconds(10));
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public async Task BuildWorkspace_DoesNotHoldWorkspaceLock_WhileCommandRuns()
    {
        var h = new Harness();

        var build = h.BuildWorkspaceAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A writer takes the exclusive per-workspace lock immediately; a build that held the
        // reader lock across the command would make this wait until the build finished.
        var writerRan = await h.Gate.RunWriteAsync(WorkspaceId, _ => Task.FromResult(true), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(writerRan);
        Assert.IsFalse(build.IsCompleted, "The build command must still be running while the writer ran.");

        h.Runner.Release();
        await build.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task BuildWorkspace_WorkspaceVersionMovedDuringRun_ReportsWorkspaceChangedDuringRun()
    {
        var h = new Harness();

        var build = h.BuildWorkspaceAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Version++;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await build.WaitAsync(TimeSpan.FromSeconds(10)));
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "workspaceChangedDuringRun" }, warnings);
        Assert.IsTrue(doc.RootElement.TryGetProperty("commandDurationMs", out _));
    }

    [TestMethod]
    public async Task BuildWorkspace_UnchangedWorkspace_HasNoWarningsAndReportsCommandDuration()
    {
        var h = new Harness();

        var build = h.BuildWorkspaceAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await build.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(doc.RootElement.TryGetProperty("warnings", out _),
            "A build against an unchanged workspace carries no warnings.");
        var duration = doc.RootElement.GetProperty("commandDurationMs").GetInt64();
        Assert.IsTrue(duration >= 0);
    }

    [TestMethod]
    public async Task BuildProject_WorkspaceVersionMovedDuringRun_ReportsWorkspaceChangedDuringRun()
    {
        var h = new Harness();

        var build = h.BuildProjectAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Version++;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await build.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(
            "workspaceChangedDuringRun",
            doc.RootElement.GetProperty("warnings")[0].GetString());
    }

    [TestMethod]
    public async Task BuildWorkspace_WorkspaceClosedDuringRun_ReturnsUnenrichedResultWithWarning()
    {
        var h = new Harness();

        var build = h.BuildWorkspaceAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Closed = true;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await build.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(
            "workspaceChangedDuringRun",
            doc.RootElement.GetProperty("warnings")[0].GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public async Task BuildWorkspace_BuildTimeoutBudgetExpiry_StillThrowsTimeoutException()
    {
        var h = new Harness(buildTimeout: TimeSpan.FromMilliseconds(100));

        // The runner never completes; the executor's total budget must expire and surface as
        // TimeoutException (the tool error filter classifies it as Timeout).
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => h.BuildWorkspaceAsync());
    }

    private sealed class ControlledCommandRunner : IDotnetCommandRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(ct);
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
    }

    private sealed class BuildWorkspaceManager : IWorkspaceManager
    {
        public int Version { get; set; } = 1;

        public bool Closed { get; set; }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();

        public bool ContainsWorkspace(string workspaceId) => !Closed;

        public bool IsStale(string workspaceId) => false;

        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: @"C:\repo\Sample.slnx",
                WorkspaceVersion: Version,
                SnapshotToken: $"{workspaceId}:{Version}",
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

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool Close(string workspaceId)
        {
            Closed = true;
            return true;
        }

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public int GetCurrentVersion(string workspaceId) =>
            Closed ? throw ((IWorkspaceManager)this).CreateWorkspaceNotFoundException(workspaceId) : Version;

        public void RestoreVersion(string workspaceId, int version) => Version = version;

        public Microsoft.CodeAnalysis.Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Microsoft.CodeAnalysis.Solution newSolution) => throw new NotSupportedException();

        public Microsoft.CodeAnalysis.Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }
}
