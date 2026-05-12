using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests.Workspace;

/// <summary>
/// Regression coverage for <c>workspace-cache-prewarm-on-load</c>: callers can opt into the
/// warm-start profile directly on <c>workspace_load</c>, while the default load path stays cold.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceCachePrewarmTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task LoadWorkspace_WithPrewarmTrue_WarmsWorkspaceAndReturnsWarmSummary()
    {
        await using var isolated = CreateIsolatedWorkspaceCopy();
        string? workspaceId = null;

        try
        {
            var json = await WorkspaceTools.LoadWorkspace(
                server: null!,
                gate: WorkspaceExecutionGate,
                workspace: WorkspaceManager,
                warmService: WorkspaceWarmService,
                commandRunner: DotnetCommandRunner,
                path: isolated.SolutionPath,
                verbose: false,
                autoRestore: false,
                prewarm: true,
                progress: null,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            workspaceId = root.GetProperty("workspaceId").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId), "workspace_load must return a workspaceId.");

            Assert.IsTrue(root.TryGetProperty("prewarm", out var prewarm),
                "prewarm=true must return the workspace_warm result block alongside the load summary.");
            Assert.AreEqual(workspaceId, prewarm.GetProperty("workspaceId").GetString(),
                "The prewarm result must target the workspace that workspace_load just returned.");
            Assert.IsTrue(prewarm.GetProperty("projectsWarmed").GetArrayLength() > 0,
                "Prewarm should visit at least one project in the sample solution.");
            Assert.IsTrue(prewarm.GetProperty("coldCompilationCount").GetInt32() > 0,
                "Prewarm should pay the cold compilation cost on a fresh isolated workspace.");

            var repeatWarm = await WorkspaceWarmService.WarmAsync(workspaceId!, projects: null, CancellationToken.None);
            Assert.AreEqual(0, repeatWarm.ColdCompilationCount,
                "After workspace_load(prewarm=true), a repeat warm should find every project already warm.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                WorkspaceManager.Close(workspaceId);
            }
        }
    }

    [TestMethod]
    public async Task LoadWorkspace_ExplicitPrewarmFalse_DoesNotWarmWorkspace()
    {
        await using var isolated = CreateIsolatedWorkspaceCopy();
        string? workspaceId = null;

        try
        {
            var json = await WorkspaceTools.LoadWorkspace(
                server: null!,
                gate: WorkspaceExecutionGate,
                workspace: WorkspaceManager,
                warmService: WorkspaceWarmService,
                commandRunner: DotnetCommandRunner,
                path: isolated.SolutionPath,
                verbose: false,
                autoRestore: false,
                prewarm: false,
                progress: null,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            workspaceId = root.GetProperty("workspaceId").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId), "workspace_load must return a workspaceId.");
            Assert.IsFalse(root.TryGetProperty("prewarm", out _),
                "The default cold-load response must not include a prewarm block.");

            var firstWarmAfterLoad = await WorkspaceWarmService.WarmAsync(workspaceId!, projects: null, CancellationToken.None);
            Assert.IsTrue(firstWarmAfterLoad.ColdCompilationCount > 0,
                "prewarm=false must preserve the cold workspace profile until the caller explicitly warms.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                WorkspaceManager.Close(workspaceId);
            }
        }
    }

    [TestMethod]
    public async Task LoadWorkspace_OmittedPrewarm_AutoWarmsAboveThreshold()
    {
        var status = CreateWorkspaceStatus(projectCount: 51);
        var workspace = new FakeWorkspaceManager(status);
        var gate = new PassthroughWorkspaceExecutionGate();
        var warmService = new RecordingWorkspaceWarmService();

        var json = await WorkspaceTools.LoadWorkspace(
            server: null!,
            gate: gate,
            workspace: workspace,
            warmService: warmService,
            commandRunner: new ThrowingDotnetCommandRunner(),
            path: @"C:\repo\Large.slnx",
            verbose: false,
            autoRestore: false,
            prewarm: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.IsTrue(root.TryGetProperty("prewarm", out var prewarm),
            "Omitted prewarm must auto-warm when the loaded solution exceeds the project-count threshold.");
        Assert.AreEqual(status.WorkspaceId, prewarm.GetProperty("workspaceId").GetString());
        Assert.AreEqual(1, warmService.CallCount);
        Assert.AreEqual(1, gate.ReadCallCount, "Auto-prewarm must run through the per-workspace read gate.");
    }

    [TestMethod]
    public async Task LoadWorkspace_OmittedPrewarm_DoesNotWarmAtThreshold()
    {
        var status = CreateWorkspaceStatus(projectCount: 50);
        var workspace = new FakeWorkspaceManager(status);
        var gate = new PassthroughWorkspaceExecutionGate();
        var warmService = new RecordingWorkspaceWarmService();

        var json = await WorkspaceTools.LoadWorkspace(
            server: null!,
            gate: gate,
            workspace: workspace,
            warmService: warmService,
            commandRunner: new ThrowingDotnetCommandRunner(),
            path: @"C:\repo\Threshold.slnx",
            verbose: false,
            autoRestore: false,
            prewarm: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("prewarm", out _),
            "The automatic warm threshold is strictly greater than 50 projects.");
        Assert.AreEqual(0, warmService.CallCount);
        Assert.AreEqual(0, gate.ReadCallCount);
    }

    [TestMethod]
    public async Task LoadWorkspace_ExplicitPrewarmFalse_DoesNotWarmAboveThreshold()
    {
        var status = CreateWorkspaceStatus(projectCount: 51);
        var workspace = new FakeWorkspaceManager(status);
        var gate = new PassthroughWorkspaceExecutionGate();
        var warmService = new RecordingWorkspaceWarmService();

        var json = await WorkspaceTools.LoadWorkspace(
            server: null!,
            gate: gate,
            workspace: workspace,
            warmService: warmService,
            commandRunner: new ThrowingDotnetCommandRunner(),
            path: @"C:\repo\Large.slnx",
            verbose: false,
            autoRestore: false,
            prewarm: false,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        Assert.IsFalse(doc.RootElement.TryGetProperty("prewarm", out _),
            "Explicit prewarm=false must opt out even for above-threshold solutions.");
        Assert.AreEqual(0, warmService.CallCount);
        Assert.AreEqual(0, gate.ReadCallCount);
    }

    private static WorkspaceStatusDto CreateWorkspaceStatus(int projectCount)
    {
        var workspaceId = $"workspace-{projectCount}";
        var projects = Enumerable.Range(1, projectCount)
            .Select(i => new ProjectStatusDto(
                Name: $"Project{i}",
                FilePath: $@"C:\repo\Project{i}\Project{i}.csproj",
                DocumentCount: 1,
                ProjectReferences: [],
                TargetFrameworks: ["net10.0"],
                IsTestProject: false,
                AssemblyName: $"Project{i}",
                OutputType: "Library"))
            .ToArray();

        return new WorkspaceStatusDto(
            WorkspaceId: workspaceId,
            LoadedPath: @"C:\repo\Sample.slnx",
            WorkspaceVersion: 1,
            SnapshotToken: $"{workspaceId}:1",
            LoadedAtUtc: DateTimeOffset.UtcNow,
            ProjectCount: projectCount,
            DocumentCount: projectCount,
            Projects: projects,
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: []);
    }

    private sealed class PassthroughWorkspaceExecutionGate : IWorkspaceExecutionGate
    {
        public int ReadCallCount { get; private set; }

        public Task<T> RunReadAsync<T>(string workspaceId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            ReadCallCount++;
            return action(ct);
        }

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

    private sealed class RecordingWorkspaceWarmService : IWorkspaceWarmService
    {
        public int CallCount { get; private set; }

        public Task<WorkspaceWarmResult> WarmAsync(string workspaceId, string[]? projects, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new WorkspaceWarmResult(
                workspaceId,
                ["Project1"],
                ElapsedMs: 1,
                ColdCompilationCount: 1));
        }
    }

    private sealed class ThrowingDotnetCommandRunner : IDotnetCommandRunner
    {
        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            throw new NotSupportedException("These tests disable autoRestore; dotnet should not be invoked.");
    }

    private sealed class FakeWorkspaceManager(WorkspaceStatusDto status) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) => Task.FromResult(status);

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException("These tests disable autoRestore; reload should not be invoked.");

        public bool ContainsWorkspace(string workspaceId) => workspaceId == status.WorkspaceId;

        public bool IsStale(string workspaceId) => false;

        public bool Close(string workspaceId) => workspaceId == status.WorkspaceId;

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [status];

        public WorkspaceStatusDto GetStatus(string workspaceId) => status;

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            Task.FromResult<string?>(null);

        public int GetCurrentVersion(string workspaceId) => status.WorkspaceVersion;

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) { }
    }
}
