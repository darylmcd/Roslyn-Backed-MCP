using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression coverage for <c>workspace-close-with-drain-processes-for-teardown</c>:
/// <see cref="WorkspaceTools.CloseWorkspace"/> with <c>drainProcesses=true</c> invokes
/// <see cref="IDotnetCommandRunner.RunAsync"/> with <c>build-server shutdown</c> after
/// the session is removed, and with <c>drainProcesses=false</c> (the default) it does not.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceCloseDrainTests
{
    // ---------------------------------------------------------------------------
    // Positive: drainProcesses=true calls build-server shutdown
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesTrue_InvokesBuildServerShutdown()
    {
        const string expectedWorkspaceId = "test-ws-drain-1";
        const string loadedPath = @"C:\repo\Sample.slnx";

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: true,
            ct: CancellationToken.None);

        // The close payload must report success.
        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean(),
            "CloseWorkspace must return success=true when the session existed.");
        Assert.AreEqual(expectedWorkspaceId, doc.RootElement.GetProperty("workspaceId").GetString());

        // The drain command must have been called exactly once.
        Assert.AreEqual(1, commandRunner.CallCount,
            "drainProcesses=true must invoke commandRunner exactly once.");

        // Verify the arguments passed to the runner.
        Assert.IsNotNull(commandRunner.LastArguments, "commandRunner must have captured arguments.");
        CollectionAssert.AreEqual(
            new[] { "build-server", "shutdown" },
            commandRunner.LastArguments!.ToArray(),
            "The drain command must pass [\"build-server\", \"shutdown\"] to the runner.");

        // Working directory must be the directory that contained the loaded path.
        Assert.AreEqual(
            Path.GetDirectoryName(loadedPath),
            commandRunner.LastWorkingDirectory,
            "The drain working directory must be the directory of the loaded path.");
    }

    // ---------------------------------------------------------------------------
    // Negative: drainProcesses=false (default) must NOT invoke commandRunner
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_DrainProcessesFalse_DoesNotInvokeCommandRunner()
    {
        const string expectedWorkspaceId = "test-ws-no-drain";
        const string loadedPath = @"C:\repo\Another.slnx";

        var status = CreateStatus(expectedWorkspaceId, loadedPath);
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: expectedWorkspaceId,
            drainProcesses: false,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(doc.RootElement.GetProperty("success").GetBoolean());

        Assert.AreEqual(0, commandRunner.CallCount,
            "drainProcesses=false must NOT invoke commandRunner.");
    }

    // ---------------------------------------------------------------------------
    // Defensive: unknown workspaceId + drainProcesses=true must NOT error on drain
    // ---------------------------------------------------------------------------

    [TestMethod]
    public async Task CloseWorkspace_UnknownWorkspaceId_DrainProcessesTrue_ReturnsFalseWithoutError()
    {
        const string unknownId = "unknown-ws-id";

        // FakeWorkspaceManagerForDrain returns false from Close when the id is unknown,
        // and throws from GetStatus — the drain path should skip gracefully.
        var fakeWorkspace = new FakeWorkspaceManagerForDrain(status: null);
        var gate = new PassthroughGate();
        var commandRunner = new RecordingDotnetCommandRunner();

        var json = await WorkspaceTools.CloseWorkspace(
            server: null!,
            gate: gate,
            workspace: fakeWorkspace,
            commandRunner: commandRunner,
            workspaceId: unknownId,
            drainProcesses: true,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        // The close returns success=false because the workspace was not found.
        Assert.IsFalse(doc.RootElement.GetProperty("success").GetBoolean(),
            "Closing an unknown workspaceId must return success=false.");

        // The drain must have been skipped — no call to commandRunner.
        Assert.AreEqual(0, commandRunner.CallCount,
            "The drain must be skipped when GetStatus throws (unknown workspace).");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static WorkspaceStatusDto CreateStatus(string workspaceId, string loadedPath) =>
        new WorkspaceStatusDto(
            WorkspaceId: workspaceId,
            LoadedPath: loadedPath,
            WorkspaceVersion: 1,
            SnapshotToken: $"{workspaceId}:1",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: 1,
            DocumentCount: 1,
            Projects: [new ProjectStatusDto(
                Name: "Proj",
                FilePath: @"C:\repo\Proj\Proj.csproj",
                DocumentCount: 1,
                ProjectReferences: [],
                TargetFrameworks: ["net10.0"],
                IsTestProject: false,
                AssemblyName: "Proj",
                OutputType: "Library")],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: []);

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class RecordingDotnetCommandRunner : IDotnetCommandRunner
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public string? LastWorkingDirectory { get; private set; }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            CallCount++;
            LastWorkingDirectory = workingDirectory;
            LastArguments = arguments;
            return Task.FromResult(new CommandExecutionDto(
                Command: "dotnet",
                Arguments: arguments,
                WorkingDirectory: workingDirectory,
                TargetPath: targetPath,
                ExitCode: 0,
                Succeeded: true,
                DurationMs: 0,
                StdOut: string.Empty,
                StdErr: string.Empty));
        }
    }

    private sealed class PassthroughGate : IWorkspaceExecutionGate
    {
        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public Task<T> RunWriteAsync<T>(
            string workspaceId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct,
            bool applyStalenessPolicy = true) =>
            action(ct);

        public Task<T> RunLoadGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            action(ct);

        public void RemoveGate(string workspaceId) { }
    }

    private sealed class FakeWorkspaceManagerForDrain(WorkspaceStatusDto? status) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, CancellationToken ct) =>
            Task.FromResult(status ?? throw new KeyNotFoundException("No status configured."));

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool ContainsWorkspace(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId;

        public bool IsStale(string workspaceId) => false;

        public bool Close(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId;

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() =>
            status is not null ? [status] : [];

        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            status is not null && workspaceId == status.WorkspaceId
                ? status
                : throw new KeyNotFoundException($"Workspace not found: {workspaceId}");

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(
            string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public int GetCurrentVersion(string workspaceId) =>
            status?.WorkspaceVersion ?? throw new KeyNotFoundException();

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) { }
    }
}
