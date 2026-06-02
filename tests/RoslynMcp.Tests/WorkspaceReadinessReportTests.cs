using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceReadinessReportTests
{
    [TestMethod]
    public async Task ReadinessReport_NoWorkspace_ReturnsLoadWorkflow()
    {
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            new FakeGate(),
            new FakeWorkspaceManager([]));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("first-run-readiness", doc.RootElement.GetProperty("reportKind").GetString());
        Assert.AreEqual("no-workspace-loaded", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("workspace").ValueKind);
        Assert.AreEqual("workspace_load",
            doc.RootElement.GetProperty("nextRecommendedWorkflows")[0].GetProperty("workflow").GetString());
    }

    [TestMethod]
    public async Task ReadinessReport_ReadyWorkspace_ReturnsReadyVerdict()
    {
        var json = await CreateReportAsync(CreateStatus());

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("reported", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("ready", GetVerdict(doc));
        StringAssert.Contains(
            string.Join(" ", ReadLimitations(doc)),
            "No tests");
        Assert.AreEqual("recommend_workflow",
            doc.RootElement.GetProperty("nextRecommendedWorkflows")[0].GetProperty("workflow").GetString());
    }

    [TestMethod]
    public async Task ReadinessReport_RestoreRequired_ReturnsRestoreNeededVerdict()
    {
        var json = await CreateReportAsync(CreateStatus(restoreRequired: true));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("restore-needed", GetVerdict(doc));
        StringAssert.Contains(ReadWorkflows(doc), "dotnet restore");
    }

    [TestMethod]
    public async Task ReadinessReport_UnresolvedAnalyzerBuildOutput_ReturnsBuildNeededBeforeRestore()
    {
        DiagnosticDto[] diagnostics =
        [
            new(
                "WORKSPACE_UNRESOLVED_ANALYZER",
                "Unresolved analyzer reference removed from project 'SampleLib': DefinitelyMissingAnalyzer.dll. This typically indicates a missing analyzer build output. Run `dotnet build` on the analyzer project, then `workspace_reload`.",
                "Warning",
                "Workspace",
                null, null, null, null, null)
        ];

        var json = await CreateReportAsync(CreateStatus(diagnostics, restoreRequired: true));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("build-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "buildRequired=true");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet build");
    }

    [TestMethod]
    public async Task ReadinessReport_AnalyzerLimited_ReturnsAnalyzerLimitedVerdict()
    {
        DiagnosticDto[] diagnostics =
        [
            new(
                "WORKSPACE_FAILURE",
                "ResolveComReference: Cannot resolve COM reference 'MSXML2'.",
                "Warning",
                "Workspace",
                null, null, null, null, null)
        ];

        var json = await CreateReportAsync(CreateStatus(diagnostics));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("analyzer-limited", GetVerdict(doc));
        StringAssert.Contains(ReadLimitations(doc), "Analyzer-driven tools may under-report");
        Assert.AreEqual("workspace_status(verbose=true)",
            doc.RootElement.GetProperty("nextRecommendedWorkflows")[0].GetProperty("workflow").GetString());
    }

    [TestMethod]
    public async Task ReadinessReport_MultipleWorkspacesWithoutId_AsksForWorkspaceId()
    {
        var json = await WorkspaceTools.GetWorkspaceReadinessReport(
            new FakeGate(),
            new FakeWorkspaceManager([CreateStatus(workspaceId: "ws-1"), CreateStatus(workspaceId: "ws-2")]));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("workspace-id-required", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("workspace_readiness_report",
            doc.RootElement.GetProperty("nextRecommendedWorkflows")[0].GetProperty("workflow").GetString());
    }

    private static Task<string> CreateReportAsync(WorkspaceStatusDto status) =>
        WorkspaceTools.GetWorkspaceReadinessReport(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            workspaceId: status.WorkspaceId);

    private static WorkspaceStatusDto CreateStatus(
        IReadOnlyList<DiagnosticDto>? diagnostics = null,
        bool restoreRequired = false,
        string workspaceId = "ws-1")
    {
        var workspaceDiagnostics = diagnostics ?? [];

        return new WorkspaceStatusDto(
            WorkspaceId: workspaceId,
            LoadedPath: Path.Combine("repo", "SampleSolution.slnx"),
            WorkspaceVersion: 7,
            SnapshotToken: "snapshot-7",
            LoadedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            ProjectCount: 2,
            DocumentCount: 4,
            Projects:
            [
                new("SampleLib", Path.Combine("repo", "SampleLib", "SampleLib.csproj"), 2, [], ["net10.0"], false, "SampleLib", "Library"),
                new("SampleLib.Tests", Path.Combine("repo", "SampleLib.Tests", "SampleLib.Tests.csproj"), 2, ["SampleLib"], ["net10.0"], true, "SampleLib.Tests", "Exe")
            ],
            IsLoaded: true,
            IsStale: false,
            WorkspaceDiagnostics: workspaceDiagnostics,
            RestoreRequired: restoreRequired);
    }

    private static string GetVerdict(JsonDocument doc) =>
        doc.RootElement.GetProperty("workspace").GetProperty("verdict").GetString()!;

    private static string ReadLimitations(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("limitations").EnumerateArray().Select(value => value.GetString()));

    private static string ReadSignals(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("workspace").GetProperty("signals").EnumerateArray().Select(value => value.GetString()));

    private static string ReadWorkflows(JsonDocument doc) =>
        string.Join(" ", doc.RootElement.GetProperty("nextRecommendedWorkflows").EnumerateArray().Select(value => value.GetProperty("workflow").GetString()));

    private sealed class FakeGate : IWorkspaceExecutionGate
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

    private sealed class FakeWorkspaceManager(IReadOnlyList<WorkspaceStatusDto> statuses) : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();

        public bool ContainsWorkspace(string workspaceId) =>
            statuses.Any(status => string.Equals(status.WorkspaceId, workspaceId, StringComparison.Ordinal));

        public bool IsStale(string workspaceId) => GetStatus(workspaceId).IsStale;

        public bool Close(string workspaceId) => throw new NotSupportedException();

        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => statuses;

        public WorkspaceStatusDto GetStatus(string workspaceId) =>
            statuses.FirstOrDefault(status => string.Equals(status.WorkspaceId, workspaceId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(workspaceId);

        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetStatus(workspaceId));

        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();

        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<GeneratedDocumentDto>>([]);

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public int GetCurrentVersion(string workspaceId) => GetStatus(workspaceId).WorkspaceVersion;

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }
}
