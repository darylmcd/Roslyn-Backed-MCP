using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Tests.TestInfrastructure;

namespace RoslynMcp.Tests;

/// <summary>
/// Covers the failure contract for <c>test_coverage</c> — backlog rows
/// <c>test-coverage-timeout-failure-envelope</c> (P2) and
/// <c>test-coverage-unexpected-error-not-iserror</c>. When the <c>dotnet test</c> runner is
/// cancelled by the gate (MCP timeout) the tool emits a typed
/// <see cref="TestCoverageFailureEnvelopeDto"/> (<c>errorKind=Timeout</c>); any other unexpected
/// exception propagates out of <see cref="TestCoverageTools.RunTestCoverageCore"/> so the shared
/// <c>StructuredCallToolFilter</c> reports it as <c>isError: true</c> (wire coverage in
/// <c>TestCoverageErrorWireTests</c>).
/// </summary>
[TestClass]
public sealed class TestCoverageFailureEnvelopeTests
{
    /// <summary>
    /// (1) OCE thrown from <c>RunAsync</c> must be classified as <c>Timeout</c> with
    /// <c>isRetryable=false</c>. The handler must not re-throw — the gate lambda
    /// contract returns a JSON string and the caller expects a structured envelope.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_RunnerThrowsOperationCanceled_EmitsTimeoutEnvelope()
    {
        var gate = new PassthroughGate();
        var workspace = new FakeWorkspaceManager();
        var runner = new ThrowingDotnetCommandRunner(new OperationCanceledException("simulated cancel"));

        var json = await TestCoverageTools.RunTestCoverageCore(
            gate,
            workspace,
            runner,
            workspaceId: "ws-coverage-timeout",
            projectName: null,
            deprecation: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.GetProperty("success").GetBoolean(),
            "Cancelled coverage runs must report success=false.");

        var envelope = root.GetProperty("failureEnvelope");
        Assert.AreEqual(JsonValueKind.Object, envelope.ValueKind,
            "Cancelled runs must populate failureEnvelope, not leave it null.");
        Assert.AreEqual("Timeout", envelope.GetProperty("errorKind").GetString());
        Assert.IsFalse(envelope.GetProperty("isRetryable").GetBoolean(),
            "Timeout/cancellation is not transient — retry without a config change is wasted.");
        StringAssert.Contains(envelope.GetProperty("summary").GetString() ?? string.Empty,
            "cancelled");
    }

    /// <summary>
    /// (2) An arbitrary <see cref="Exception"/> from <c>RunAsync</c> must propagate (not be
    /// recovered into a success-shaped envelope) so the shared filter formats it as isError.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_RunnerThrowsUnexpected_PropagatesToSharedFilter()
    {
        var gate = new PassthroughGate();
        var workspace = new FakeWorkspaceManager();
        var runner = new ThrowingDotnetCommandRunner(new InvalidOperationException("runner exploded"));

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TestCoverageTools.RunTestCoverageCore(
                gate,
                workspace,
                runner,
                workspaceId: "ws-coverage-unknown",
                projectName: null,
                deprecation: null,
                progress: null,
                ct: CancellationToken.None));
        Assert.AreEqual("runner exploded", thrown.Message);
    }

    /// <summary>
    /// (3) Regression guard — a normal <c>RunAsync</c> return with <c>Succeeded=false</c>
    /// and <c>ExitCode=1</c> must still emit the pre-existing <c>TestFailure</c> envelope
    /// at <c>TestCoverageTools.cs:104-117</c>. Adding the new exception-path catches must
    /// not shadow the no-coverage-file fallback for a successfully-invoked runner.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_RunnerReturnsExit1NoCoverageFile_EmitsTestFailureEnvelope()
    {
        var gate = new PassthroughGate();
        var workspace = new FakeWorkspaceManager();
        var failingExecution = new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test", "sample.csproj"],
            WorkingDirectory: "C:/fake/workdir",
            TargetPath: "C:/fake/workdir/sample.csproj",
            ExitCode: 1,
            Succeeded: false,
            DurationMs: 1234,
            StdOut: "Failing test detected",
            StdErr: string.Empty);
        var runner = new StaticDotnetCommandRunner(failingExecution);

        var json = await TestCoverageTools.RunTestCoverageCore(
            gate,
            workspace,
            runner,
            workspaceId: "ws-coverage-testfailure",
            projectName: null,
            deprecation: null,
            progress: null,
            ct: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.IsFalse(root.GetProperty("success").GetBoolean());

        var envelope = root.GetProperty("failureEnvelope");
        Assert.AreEqual(JsonValueKind.Object, envelope.ValueKind);
        Assert.AreEqual("TestFailure", envelope.GetProperty("errorKind").GetString(),
            "Pre-existing TestFailure path must remain reachable — the new catch blocks " +
            "guard only the exception path, not the no-coverage-file fallback.");
        Assert.IsTrue(envelope.GetProperty("isRetryable").GetBoolean(),
            "TestFailure is retryable once the test is fixed.");
    }

    /// <summary>
    /// workspace-fork-apply-robustness-cancellation: genuine caller cancellation (the
    /// passed-in <c>ct</c> is already cancelled) must propagate as
    /// <see cref="OperationCanceledException"/> rather than being misreported as the
    /// <c>Timeout</c> envelope built for the gate's internal timeout. This is the
    /// inverse of the test above — same simulated exception from the runner, but with
    /// the caller's own token cancelled beforehand.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_CallerCancelled_PropagatesWithoutWrapping()
    {
        var gate = new PassthroughGate();
        var workspace = new FakeWorkspaceManager();
        var runner = new ThrowingDotnetCommandRunner(new OperationCanceledException("simulated caller cancel"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            TestCoverageTools.RunTestCoverageCore(
                gate,
                workspace,
                runner,
                workspaceId: "ws-coverage-caller-cancelled",
                projectName: null,
                deprecation: null,
                progress: null,
                ct: cts.Token));
    }

    [TestMethod]
    public async Task RunTestCoverageCore_StatusThrows_PropagatesToSharedFilter()
    {
        var gate = new PassthroughGate();
        var workspace = new ThrowingStatusWorkspaceManager(new InvalidOperationException("workspace status unavailable"));
        var runner = new StaticDotnetCommandRunner(new CommandExecutionDto(
            Command: "dotnet",
            Arguments: ["test"],
            WorkingDirectory: "C:/fake/workdir",
            TargetPath: "C:/fake/workdir/sample.csproj",
            ExitCode: 0,
            Succeeded: true,
            DurationMs: 1,
            StdOut: string.Empty,
            StdErr: string.Empty));

        var thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TestCoverageTools.RunTestCoverageCore(
                gate,
                workspace,
                runner,
                workspaceId: "ws-coverage-status-throws",
                projectName: null,
                deprecation: null,
                progress: null,
                ct: CancellationToken.None));
        Assert.AreEqual("workspace status unavailable", thrown.Message);
    }

    /// <summary>
    /// (5) test-coverage-temp-dir-leak (workspace-fork-apply-security-hardening): even when the
    /// runner throws mid-run, the per-run temp coverage dir must be deleted by the finally block.
    /// The runner creates the <c>--results-directory</c> on disk (as <c>dotnet test</c> would) then
    /// throws; after the exception propagates, the dir must be gone.
    /// </summary>
    [TestMethod]
    public async Task RunTestCoverageCore_RunnerThrowsAfterCreatingDir_StillDeletesTempCoverageDir()
    {
        var gate = new PassthroughGate();
        var workspace = new FakeWorkspaceManager();
        var runner = new DirCreatingThrowingDotnetCommandRunner(new InvalidOperationException("boom"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            TestCoverageTools.RunTestCoverageCore(
                gate,
                workspace,
                runner,
                workspaceId: "ws-coverage-cleanup-throw",
                projectName: null,
                deprecation: null,
                progress: null,
                ct: CancellationToken.None));

        Assert.IsNotNull(runner.CreatedResultsDirectory,
            "Runner should have created and captured the --results-directory.");
        Assert.IsFalse(Directory.Exists(runner.CreatedResultsDirectory!),
            "The temp coverage dir must be deleted by the finally block even on the exception path.");
    }

    // ── Test doubles ────────────────────────────────────────────────────────

    /// <summary>
    /// Runner that creates the <c>--results-directory</c> on disk (mirroring <c>dotnet test</c>)
    /// and then throws, exercising the finally-block cleanup on the exception path.
    /// </summary>
    private sealed class DirCreatingThrowingDotnetCommandRunner : IDotnetCommandRunner
    {
        private readonly Exception _toThrow;

        public DirCreatingThrowingDotnetCommandRunner(Exception toThrow) => _toThrow = toThrow;

        public string? CreatedResultsDirectory { get; private set; }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            for (var i = 0; i < arguments.Count - 1; i++)
            {
                if (string.Equals(arguments[i], "--results-directory", StringComparison.Ordinal))
                {
                    CreatedResultsDirectory = arguments[i + 1];
                    Directory.CreateDirectory(CreatedResultsDirectory);
                    break;
                }
            }

            throw _toThrow;
        }
    }

    /// <summary>
    /// Minimal stand-in for <see cref="IWorkspaceExecutionGate"/> that invokes the lambda
    /// directly with the caller-supplied cancellation token. Mirrors the
    /// <c>PassthroughWorkspaceExecutionGate</c> shape from <c>WorkspaceCachePrewarmTests</c>.
    /// </summary>
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

    /// <summary>
    /// Workspace stub that returns a status with an empty project list so
    /// <c>FindTestProjectsWithoutCoverlet</c> short-circuits without touching disk
    /// and execution proceeds into the <c>commandRunner.RunAsync</c> call we want
    /// to exercise.
    /// </summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => true;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => BuildStatus(workspaceId);
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(BuildStatus(workspaceId));
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;

        private static WorkspaceStatusDto BuildStatus(string workspaceId) =>
            new(
                WorkspaceId: workspaceId,
                LoadedPath: "C:\\fake\\workspace\\Sample.slnx",
                WorkspaceVersion: 1,
                SnapshotToken: "snapshot",
                LoadedAtUtc: DateTimeOffset.UtcNow,
                ProjectCount: 0,
                DocumentCount: 0,
                Projects: [],
                IsLoaded: true,
                IsStale: false,
                WorkspaceDiagnostics: []);
    }

    private sealed class ThrowingStatusWorkspaceManager : IWorkspaceManager
    {
        private readonly Exception _exception;

        public ThrowingStatusWorkspaceManager(Exception exception)
        {
            _exception = exception;
        }

        public event Action<string>? WorkspaceClosed { add { } remove { } }
        public event Action<string>? WorkspaceReloaded { add { } remove { } }

        public Task<WorkspaceStatusDto> LoadAsync(string path, EvictPolicy evictPolicy, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<WorkspaceStatusDto> ReloadAsync(string workspaceId, CancellationToken ct) =>
            throw new NotSupportedException();
        public bool ContainsWorkspace(string workspaceId) => true;
        public bool IsStale(string workspaceId) => false;
        public bool Close(string workspaceId) => throw new NotSupportedException();
        public IReadOnlyList<WorkspaceStatusDto> ListWorkspaces() => [];
        public WorkspaceStatusDto GetStatus(string workspaceId) => throw _exception;
        public Task<WorkspaceStatusDto> GetStatusAsync(string workspaceId, CancellationToken cancellationToken = default) =>
            throw _exception;
        public ProjectGraphDto GetProjectGraph(string workspaceId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeneratedDocumentDto>> GetSourceGeneratedDocumentsAsync(string workspaceId, string? projectName, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string?> GetSourceTextAsync(string workspaceId, string filePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public int GetCurrentVersion(string workspaceId) => 1;
        public void RestoreVersion(string workspaceId, int version) => throw new NotSupportedException();
        public Solution GetCurrentSolution(string workspaceId) => throw new NotSupportedException();
        public bool TryApplyChanges(string workspaceId, Solution newSolution) => throw new NotSupportedException();
        public Project? GetProject(string workspaceId, string projectNameOrPath) => null;
    }

    /// <summary>
    /// Runner stub that always throws the configured exception when <c>RunAsync</c> is
    /// invoked. Mirrors the <c>HangingDotnetCommandRunner</c> hand-rolled-stub style from
    /// <c>HardeningBehaviorTests</c> rather than reaching for NSubstitute.
    /// </summary>
    private sealed class ThrowingDotnetCommandRunner : IDotnetCommandRunner
    {
        private readonly Exception _toThrow;

        public ThrowingDotnetCommandRunner(Exception toThrow)
        {
            _toThrow = toThrow;
        }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            throw _toThrow;
    }

    /// <summary>
    /// Runner stub that returns a pre-built <see cref="CommandExecutionDto"/>. Used by
    /// the regression-guard test to exercise the existing exit-code-1 no-coverage-file
    /// fallback without triggering the new exception-path catches.
    /// </summary>
    private sealed class StaticDotnetCommandRunner : IDotnetCommandRunner
    {
        private readonly CommandExecutionDto _result;

        public StaticDotnetCommandRunner(CommandExecutionDto result)
        {
            _result = result;
        }

        public Task<CommandExecutionDto> RunAsync(
            string workingDirectory,
            string targetPath,
            IReadOnlyList<string> arguments,
            CancellationToken ct) =>
            Task.FromResult(_result);
    }
}
