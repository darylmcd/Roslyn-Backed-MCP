using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class WorkspaceSupportBundleTests
{
    [TestMethod]
    public async Task WorkspaceSupportBundle_NoWorkspace_ReturnsRecoveryHint()
    {
        var result = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(EmptyDiagnostics()));

        Assert.IsNotNull(result.StructuredContent);
        var structured = result.StructuredContent.Value;
        Assert.IsTrue(GeneratedJsonSchemaMatcher.Matches(
            structured,
            ToolOutputSchemaIndex.GetSchema("workspace_support_bundle")!));

        using var doc = JsonDocument.Parse(result.TextPayload());
        Assert.AreEqual(doc.RootElement.GetRawText(), structured.GetRawText());
        Assert.AreEqual("incident-support", doc.RootElement.GetProperty("bundleKind").GetString());
        Assert.AreEqual("no-workspace-loaded", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, doc.RootElement.GetProperty("workspace").ValueKind);
        StringAssert.Contains(
            doc.RootElement.GetProperty("nextActions")[0].GetString()!,
            "workspace_load");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_CleanWorkspace_ReturnsCappedIncidentContract()
    {
        var status = CreateStatus(isReady: true);

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(EmptyDiagnostics()));

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("clean", doc.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(doc.RootElement.GetProperty("limits").GetProperty("sourceSnippetsIncluded").GetBoolean());
        Assert.AreEqual(status.LoadedPath, doc.RootElement.GetProperty("workspace").GetProperty("loadedPath").GetString());
        Assert.AreEqual(ServerSurfaceCatalog.CatalogVersion,
            doc.RootElement.GetProperty("workspace").GetProperty("version").GetProperty("catalogVersion").GetString());
        Assert.AreEqual(0, doc.RootElement.GetProperty("diagnosticsTotals").GetProperty("totalErrors").GetInt32());
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_MultipleWorkspacesWithoutId_ReturnsIdRequiredHint()
    {
        var first = CreateStatus(isReady: true, workspaceId: "ws-1");
        var second = CreateStatus(isReady: true, workspaceId: "ws-2");

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([first, second]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(EmptyDiagnostics()));

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("workspace-id-required", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("loadedWorkspaces").GetArrayLength());
        StringAssert.Contains(
            doc.RootElement.GetProperty("nextActions")[0].GetString()!,
            "workspaceId");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_StaleWorkspace_ReturnsDriftAndReloadHint()
    {
        var status = CreateStatus(isReady: false, isStale: true);
        var drift = new WorkspaceDriftResult(
            true,
            ["C:/repo/a.cs", "C:/repo/b.cs", "C:/repo/c.cs"],
            "reload");

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(drift),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(EmptyDiagnostics()),
            maxDriftedFiles: 2);

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("stale", doc.RootElement.GetProperty("status").GetString());
        var driftJson = doc.RootElement.GetProperty("workspace").GetProperty("drift");
        Assert.IsTrue(driftJson.GetProperty("stale").GetBoolean());
        Assert.AreEqual(3, driftJson.GetProperty("totalDriftedFiles").GetInt32());
        Assert.AreEqual(2, driftJson.GetProperty("returnedDriftedFiles").GetInt32());
        Assert.IsTrue(driftJson.GetProperty("hasMore").GetBoolean());
        StringAssert.Contains(
            string.Join(" ", doc.RootElement.GetProperty("nextActions").EnumerateArray().Select(a => a.GetString())),
            "workspace_reload");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_ChangedWorkspace_ReturnsRecentLedgerAndValidationHint()
    {
        var status = CreateStatus(isReady: true);
        WorkspaceChangeDto[] changes =
        [
            new(1, "first", ["C:/repo/a.cs"], "first_tool", DateTime.UtcNow.AddMinutes(-2)),
            new(2, "second", ["C:/repo/b.cs"], "second_tool", DateTime.UtcNow.AddMinutes(-1))
        ];

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker(changes),
            new FakeDiagnosticService(EmptyDiagnostics()),
            maxChangeEntries: 1);

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("changed", doc.RootElement.GetProperty("status").GetString());
        var ledger = doc.RootElement.GetProperty("changeLedger");
        Assert.AreEqual(2, ledger.GetProperty("totalChanges").GetInt32());
        Assert.AreEqual(1, ledger.GetProperty("returnedChanges").GetInt32());
        Assert.IsTrue(ledger.GetProperty("hasMore").GetBoolean());
        Assert.AreEqual(2, ledger.GetProperty("changes")[0].GetProperty("sequenceNumber").GetInt32());
        StringAssert.Contains(
            string.Join(" ", doc.RootElement.GetProperty("nextActions").EnumerateArray().Select(a => a.GetString())),
            "validate_recent_git_changes");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_DiagnosticsPresent_ReturnsTotalsAndDiagnosticHint()
    {
        var status = CreateStatus(isReady: true);
        var diagnostics = new DiagnosticsResultDto(
            WorkspaceDiagnostics: [],
            CompilerDiagnostics: [],
            AnalyzerDiagnostics: [],
            TotalErrors: 2,
            TotalWarnings: 3,
            TotalInfo: 4,
            CompilerErrors: 1,
            AnalyzerErrors: 1,
            WorkspaceErrors: 0);

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(diagnostics));

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("attention-needed", doc.RootElement.GetProperty("status").GetString());
        var totals = doc.RootElement.GetProperty("diagnosticsTotals");
        Assert.AreEqual(2, totals.GetProperty("totalErrors").GetInt32());
        Assert.AreEqual(1, totals.GetProperty("compilerErrors").GetInt32());
        Assert.AreEqual(1, totals.GetProperty("analyzerErrors").GetInt32());
        StringAssert.Contains(
            string.Join(" ", doc.RootElement.GetProperty("nextActions").EnumerateArray().Select(a => a.GetString())),
            "compile_check");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_WarningOnlyDiagnostics_ReturnsAttentionNeeded()
    {
        var status = CreateStatus(isReady: true);
        var diagnostics = new DiagnosticsResultDto(
            WorkspaceDiagnostics: [],
            CompilerDiagnostics: [],
            AnalyzerDiagnostics: [],
            TotalErrors: 0,
            TotalWarnings: 2,
            TotalInfo: 0,
            CompilerErrors: 0,
            AnalyzerErrors: 0,
            WorkspaceErrors: 0);

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(diagnostics));

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("attention-needed", doc.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, doc.RootElement.GetProperty("diagnosticsTotals").GetProperty("totalWarnings").GetInt32());
        StringAssert.Contains(
            string.Join(" ", doc.RootElement.GetProperty("nextActions").EnumerateArray().Select(a => a.GetString())),
            "project_diagnostics");
    }

    [TestMethod]
    public async Task WorkspaceSupportBundle_InfoOnlyDiagnostics_ReturnsAttentionNeededWithAction()
    {
        var status = CreateStatus(isReady: true);
        var diagnostics = new DiagnosticsResultDto(
            WorkspaceDiagnostics: [],
            CompilerDiagnostics: [],
            AnalyzerDiagnostics: [],
            TotalErrors: 0,
            TotalWarnings: 0,
            TotalInfo: 3,
            CompilerErrors: 0,
            AnalyzerErrors: 0,
            WorkspaceErrors: 0);

        var json = await WorkspaceTools.GetWorkspaceSupportBundle(
            new FakeGate(),
            new FakeWorkspaceManager([status]),
            new FakeDriftService(new WorkspaceDriftResult(false, [], "noop")),
            new FakeChangeTracker([]),
            new FakeDiagnosticService(diagnostics));

        using var doc = JsonDocument.Parse(json.TextPayload());
        Assert.AreEqual("attention-needed", doc.RootElement.GetProperty("status").GetString());
        StringAssert.Contains(
            string.Join(" ", doc.RootElement.GetProperty("nextActions").EnumerateArray().Select(a => a.GetString())),
            "Info");
    }

    private static WorkspaceStatusDto CreateStatus(bool isReady, bool isStale = false, string workspaceId = "ws-1")
    {
        DiagnosticDto[] diagnostics = isReady
            ? []
            : [new DiagnosticDto("WORKSPACE_STALE", "Workspace is stale", "Warning", "Workspace", null, null, null, null, null)];

        return new WorkspaceStatusDto(
            WorkspaceId: workspaceId,
            LoadedPath: "C:/repo/SampleSolution.slnx",
            WorkspaceVersion: 7,
            SnapshotToken: "snapshot-7",
            LoadedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            ProjectCount: 1,
            DocumentCount: 2,
            Projects: [],
            IsLoaded: true,
            IsStale: isStale,
            WorkspaceDiagnostics: diagnostics);
    }

    private static DiagnosticsResultDto EmptyDiagnostics() =>
        new([], [], [], TotalErrors: 0, TotalWarnings: 0, TotalInfo: 0);

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
            throw new NotSupportedException();

        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();

        public int GetCurrentVersion(string workspaceId) => GetStatus(workspaceId).WorkspaceVersion;

        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();

        public Project? GetProject(string workspaceId, string projectNameOrPath) => throw new NotSupportedException();

        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();

        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
    }

    private sealed class FakeDriftService(WorkspaceDriftResult result) : IWorkspaceDriftService
    {
        public Task<WorkspaceDriftResult> CheckDriftAsync(string workspaceId, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class FakeChangeTracker(IReadOnlyList<WorkspaceChangeDto> changes) : IChangeTracker
    {
        public event Action<ChangeRecordedEventArgs>? ChangeRecorded { add { } remove { } }

        public void RecordChange(string workspaceId, string description, IReadOnlyList<string> affectedFiles, string toolName) =>
            throw new NotSupportedException();

        public IReadOnlyList<WorkspaceChangeDto> GetChanges(string workspaceId) => changes;

        public void Clear(string workspaceId) { }
    }

    private sealed class FakeDiagnosticService(DiagnosticsResultDto result) : IDiagnosticService
    {
        public Task<DiagnosticsResultDto> GetDiagnosticsAsync(
            string workspaceId,
            string? projectFilter,
            string? fileFilter,
            string? severityFilter,
            string? diagnosticIdFilter,
            CancellationToken ct) =>
            Task.FromResult(result);

        public Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
            string workspaceId,
            string diagnosticId,
            string filePath,
            int line,
            int column,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
