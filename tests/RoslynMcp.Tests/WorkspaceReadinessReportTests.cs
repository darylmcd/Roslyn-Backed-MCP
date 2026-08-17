using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceReadinessReportTests
{
    [TestMethod]
    public async Task ReadinessReport_NoWorkspace_ReturnsLoadWorkflow()
    {
        var result = await WorkspaceTools.GetWorkspaceReadinessReport(
            new FakeGate(),
            new FakeWorkspaceManager([]));

        Assert.IsNotNull(result.StructuredContent);
        var structured = result.StructuredContent.Value;
        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(
            structured,
            ToolOutputSchemaIndex.GetSchema("workspace_readiness_report")!));

        using var doc = JsonDocument.Parse(result.TextPayload());
        Assert.AreEqual(doc.RootElement.GetRawText(), structured.GetRawText());
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

        // restore-required-vs-build-conflation: a missing analyzer build output sets buildRequired,
        // NOT restoreRequired — the remedy is `dotnet build`, not a no-op `dotnet restore`. The
        // verdict must be build-needed with restoreRequired surfaced as false.
        var json = await CreateReportAsync(CreateStatus(diagnostics, buildRequired: true));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("build-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "buildRequired=true");
        StringAssert.Contains(ReadSignals(doc), "restoreRequired=false");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet build");
    }

    [TestMethod]
    public async Task ReadinessReport_UnresolvedAnalyzerWithoutMagicWording_UsesSharedClassifier()
    {
        DiagnosticDto[] diagnostics =
        [
            new(
                "WORKSPACE_UNRESOLVED_ANALYZER",
                "Analyzer reference unavailable.",
                "Warning",
                "Workspace",
                null, null, null, null, null)
        ];
        var buildRequired = WorkspaceDiagnosticClassifier.HasBuildRequired(diagnostics);
        var json = await CreateReportAsync(CreateStatus(diagnostics, buildRequired: false));

        using var doc = JsonDocument.Parse(json);
        Assert.IsTrue(buildRequired);
        Assert.AreEqual("build-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "buildRequired=true");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet build");
    }

    [TestMethod]
    public async Task ReadinessReport_NuGetRestoreWithoutAnalyzerWarning_ReturnsRestoreNeededAndNotBuildRequired()
    {
        // restore-required-vs-build-conflation regression: a genuine missing-NuGet-package case
        // (no WORKSPACE_UNRESOLVED_ANALYZER warning) must still route to restore-needed and must
        // NOT be misclassified as build-required. Proves the restore path is unaffected by the
        // build/restore split.
        DiagnosticDto[] diagnostics =
        [
            new("CS0234", "The type or namespace name 'Json' does not exist in the namespace 'System.Text'", "Error", "Workspace", null, null, null, null, null)
        ];

        var json = await CreateReportAsync(CreateStatus(diagnostics, restoreRequired: true));

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("restore-needed", GetVerdict(doc));
        StringAssert.Contains(ReadSignals(doc), "restoreRequired=true");
        Assert.IsFalse(ReadSignals(doc).Contains("buildRequired=true", StringComparison.Ordinal),
            "a NuGet-restore case without an analyzer warning must not be flagged buildRequired");
        StringAssert.Contains(ReadWorkflows(doc), "dotnet restore");
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

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("workspace-id-required", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("workspace_readiness_report",
            doc.RootElement.GetProperty("nextRecommendedWorkflows")[0].GetProperty("workflow").GetString());
    }

    [TestMethod]
    public async Task ReadinessReport_SourceGeneratedProbeFailure_IsSecretSafeAndCorrelated()
    {
        const string sentinel = "SECRET-SENTINEL-C:/private/generated.cs";
        var status = CreateStatus();
        var sink = new CapturingServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);

        string json;
        using (RequestCorrelationContext.Begin())
        {
            json = (await WorkspaceTools.GetWorkspaceReadinessReport(
                new FakeGate(),
                new FakeWorkspaceManager(
                    [status],
                    new InvalidOperationException(sentinel, new IOException(sentinel))),
                workspaceId: status.WorkspaceId,
                ct: CancellationToken.None,
                exceptionReporter: reporter)).TextPayload();
        }

        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("reported", doc.RootElement.GetProperty("status").GetString());
        var limitations = ReadLimitations(doc);
        StringAssert.Contains(limitations, "source_generated_documents");
        StringAssert.Contains(limitations, "InternalError");
        StringAssert.Contains(limitations, "correlationId=");
        Assert.IsFalse(json.Contains(sentinel, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));

        Assert.HasCount(1, sink.Events);
        Assert.AreEqual("WorkspaceReadiness", sink.Events.Single().Category);
        Assert.HasCount(2, sink.Events.Single().Exception.ExceptionTypes);
        Assert.IsFalse(JsonSerializer.Serialize(sink.Events.Single()).Contains(sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadinessReport_SourceGeneratedProbeCancellation_Propagates()
    {
        var status = CreateStatus();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            WorkspaceTools.GetWorkspaceReadinessReport(
                new FakeGate(),
                new FakeWorkspaceManager([status], new OperationCanceledException()),
                workspaceId: status.WorkspaceId));
    }

    private static async Task<string> CreateReportAsync(WorkspaceStatusDto status) =>
        (await WorkspaceTools.GetWorkspaceReadinessReport(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            workspaceId: status.WorkspaceId)).TextPayload();

    private static WorkspaceStatusDto CreateStatus(
        IReadOnlyList<DiagnosticDto>? diagnostics = null,
        bool restoreRequired = false,
        bool buildRequired = false,
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
            RestoreRequired: restoreRequired,
            BuildRequired: buildRequired);
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

    private sealed class FakeWorkspaceManager(
        IReadOnlyList<WorkspaceStatusDto> statuses,
        Exception? sourceGeneratedFailure = null) : IWorkspaceManager
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
            sourceGeneratedFailure is null
                ? Task.FromResult<IReadOnlyList<GeneratedDocumentDto>>([])
                : Task.FromException<IReadOnlyList<GeneratedDocumentDto>>(sourceGeneratedFailure);

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public int GetCurrentVersion(string workspaceId) => GetStatus(workspaceId).WorkspaceVersion;

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }
}
