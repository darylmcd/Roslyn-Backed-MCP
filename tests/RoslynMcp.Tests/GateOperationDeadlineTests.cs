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
/// build_workspace / build_project / test_run / test_coverage must not hold the workspace gate
/// across the dotnet command: the gate's 2-minute request timeout (armed before the lock wait)
/// would otherwise beat the 5-minute <c>BuildTimeout</c> / 10-minute <c>TestTimeout</c> and pin a
/// throttle slot plus the reader lock for the whole command.
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
        public TestRunnerService TestService { get; }
        public GatedCommandExecutor Executor { get; }
        public ValidationServiceOptions Options { get; }
        public NuGetDependencyService ScanService { get; }

        public Harness(TimeSpan? buildTimeout = null, TimeSpan? testTimeout = null, TimeSpan? scanTimeout = null)
        {
            Gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), Manager, Clock);
            Executor = new GatedCommandExecutor(Manager, Runner, NullLogger<GatedCommandExecutor>.Instance);
            Options = new ValidationServiceOptions
            {
                BuildTimeout = buildTimeout ?? TimeSpan.FromMinutes(5),
                TestTimeout = testTimeout ?? TimeSpan.FromMinutes(10),
                VulnerabilityScanTimeout = scanTimeout ?? TimeSpan.FromMinutes(5),
            };
            ScanService = new NuGetDependencyService(
                Manager,
                Executor,
                new MsBuildEvaluationService(Manager),
                NullLogger<NuGetDependencyService>.Instance,
                Options);
            Service = new BuildService(
                Manager, Executor, new CompilationCache(Manager), NullLogger<BuildService>.Instance, Options);
            TestService = new TestRunnerService(
                Manager,
                Executor,
                NullLogger<TestRunnerService>.Instance,
                new UnusedTestDiscoveryService(),
                Options);
        }

        public Task<string> RunTestsAsync() =>
            ValidationTools.RunTests(Gate, TestService, WorkspaceId, ct: CancellationToken.None);

        public Task<string> RunCoverageAsync() =>
            TestCoverageTools.RunTestCoverage(
                Gate, Manager, Executor, Options, WorkspaceId, projectName: null, progress: null, ct: CancellationToken.None);

        public Task<string> BuildWorkspaceAsync() =>
            ValidationTools.BuildWorkspace(Gate, Service, WorkspaceId, progress: null, CancellationToken.None);

        public Task<string> BuildProjectAsync() =>
            ValidationTools.BuildProject(Gate, Service, WorkspaceId, "Sample", CancellationToken.None);

        public Task<string> ScanVulnerabilitiesAsync() =>
            SecurityTools.ScanNuGetVulnerabilities(
                Gate, ScanService, WorkspaceId, projectName: null, includeTransitive: false, progress: null, CancellationToken.None);
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

    [TestMethod]
    public async Task TestRun_RunsPastGateRequestTimeout_WhenTestsAreStillWithinTestTimeout()
    {
        var h = new Harness();

        var run = h.RunTestsAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
        Assert.IsTrue(doc.RootElement.GetProperty("failureEnvelope").ValueKind == JsonValueKind.Null);
    }

    [TestMethod]
    public async Task TestRun_DoesNotHoldWorkspaceLock_WhileCommandRuns()
    {
        var h = new Harness();

        var run = h.RunTestsAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var writerRan = await h.Gate.RunWriteAsync(WorkspaceId, _ => Task.FromResult(true), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(writerRan);
        Assert.IsFalse(run.IsCompleted, "The test command must still be running while the writer ran.");

        h.Runner.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task TestRun_WorkspaceVersionMovedDuringRun_ReportsWorkspaceChangedDuringRun()
    {
        var h = new Harness();

        var run = h.RunTestsAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Version++;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        var warnings = doc.RootElement.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "workspaceChangedDuringRun" }, warnings);
        Assert.IsTrue(doc.RootElement.TryGetProperty("commandDurationMs", out _));
    }

    [TestMethod]
    public async Task TestRun_UnchangedWorkspace_HasNoWarningsAndReportsCommandDuration()
    {
        var h = new Harness();

        var run = h.RunTestsAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(doc.RootElement.TryGetProperty("warnings", out _),
            "A test run against an unchanged workspace carries no warnings.");
        Assert.IsGreaterThanOrEqualTo(0L, doc.RootElement.GetProperty("commandDurationMs").GetInt64());
    }

    [TestMethod]
    public async Task TestRun_WorkspaceClosedDuringRun_ReturnsParsedResultWithWarning()
    {
        var h = new Harness();

        var run = h.RunTestsAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Closed = true;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await run.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(
            "workspaceChangedDuringRun",
            doc.RootElement.GetProperty("warnings")[0].GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public async Task TestRun_TestTimeoutBudgetExpiry_ReturnsTimeoutFailureEnvelope()
    {
        var h = new Harness(testTimeout: TimeSpan.FromMilliseconds(100));

        // The runner never completes; the executor's total budget expires and test_run keeps its
        // structured Timeout envelope (not an isError frame).
        using var doc = JsonDocument.Parse(await h.RunTestsAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        var envelope = doc.RootElement.GetProperty("failureEnvelope");
        Assert.AreEqual("Timeout", envelope.GetProperty("errorKind").GetString());
        Assert.IsFalse(doc.RootElement.GetProperty("execution").GetProperty("succeeded").GetBoolean());
    }

    [TestMethod]
    public async Task TestCoverage_RunsPastGateRequestTimeout_WhenCoverageIsStillWithinTestTimeout()
    {
        var h = new Harness();

        var coverage = h.RunCoverageAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Runner.Release();

        // The fake runner writes no coverage file, so a command that ran to completion reports
        // CoverletMissing; a gate timeout would have reported Timeout.
        using var doc = JsonDocument.Parse(await coverage.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual("CoverletMissing", doc.RootElement.GetProperty("failureEnvelope").GetProperty("errorKind").GetString());
    }

    [TestMethod]
    public async Task TestCoverage_DoesNotHoldWorkspaceLock_WhileCommandRuns()
    {
        var h = new Harness();

        var coverage = h.RunCoverageAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var writerRan = await h.Gate.RunWriteAsync(WorkspaceId, _ => Task.FromResult(true), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(writerRan);
        Assert.IsFalse(coverage.IsCompleted, "The coverage command must still be running while the writer ran.");

        h.Runner.Release();
        await coverage.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task TestCoverage_TestTimeoutBudgetExpiry_ReturnsTimeoutFailureEnvelope()
    {
        var h = new Harness(testTimeout: TimeSpan.FromMilliseconds(100));

        using var doc = JsonDocument.Parse(await h.RunCoverageAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.AreEqual("Timeout", doc.RootElement.GetProperty("failureEnvelope").GetProperty("errorKind").GetString());
    }

    [TestMethod]
    public async Task VulnerabilityScan_RunsPastGateRequestTimeout_WhenScanIsStillWithinScanTimeout()
    {
        var h = new Harness();

        var scan = h.ScanVulnerabilitiesAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Clock.Advance(TimeSpan.FromMinutes(3));
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await scan.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(0, doc.RootElement.GetProperty("totalVulnerabilities").GetInt32());
    }

    [TestMethod]
    public async Task VulnerabilityScan_DoesNotHoldWorkspaceLock_WhileCommandRuns()
    {
        var h = new Harness();

        var scan = h.ScanVulnerabilitiesAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var writerRan = await h.Gate.RunWriteAsync(WorkspaceId, _ => Task.FromResult(true), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(writerRan);
        Assert.IsFalse(scan.IsCompleted, "The scan command must still be running while the writer ran.");

        h.Runner.Release();
        await scan.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task VulnerabilityScan_ScanTimeoutBudgetExpiry_StillThrowsTimeoutException()
    {
        var h = new Harness(scanTimeout: TimeSpan.FromMilliseconds(100));

        // The runner never completes; the executor's total budget must expire and surface as
        // TimeoutException (the tool error filter classifies it as Timeout).
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => h.ScanVulnerabilitiesAsync());
    }

    [TestMethod]
    public async Task VulnerabilityScan_CachedResult_SkipsTheCommand()
    {
        var h = new Harness();

        var first = h.ScanVulnerabilitiesAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Runner.Release();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, h.Runner.CallCount);

        await h.ScanVulnerabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, h.Runner.CallCount, "An unchanged workspace must be served from the scan cache.");
    }

    [TestMethod]
    public async Task VulnerabilityScan_WorkspaceVersionMovedDuringRun_DoesNotCacheTheResult()
    {
        var h = new Harness();

        var first = h.ScanVulnerabilitiesAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Version++;
        h.Runner.Release();
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        // The result was resolved against the old package references, so the next call at the
        // new version must run the command again instead of reusing it.
        await h.ScanVulnerabilitiesAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, h.Runner.CallCount);
    }

    [TestMethod]
    public async Task VulnerabilityScan_WorkspaceClosedDuringRun_StillReturnsTheResult()
    {
        var h = new Harness();

        var scan = h.ScanVulnerabilitiesAsync();
        await h.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        h.Manager.Closed = true;
        h.Runner.Release();

        using var doc = JsonDocument.Parse(await scan.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(0, doc.RootElement.GetProperty("totalVulnerabilities").GetInt32());
    }

    private sealed class UnusedTestDiscoveryService : ITestDiscoveryService
    {
        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(
            string workspaceId, SymbolLocator locator, int maxResults, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(
            string workspaceId, IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledCommandRunner : IDotnetCommandRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        private int _callCount;

        public void Release() => _release.TrySetResult();

        public async Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
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
                StdOut: "{}",
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
                        IsTestProject: true,
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
