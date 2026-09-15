using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Support;

namespace RoslynMcp.Tests;

/// <summary>Exercises aggregate verdicts through both entry points with real workspace and Git scope.</summary>
[TestClass]
public sealed class WorkspaceValidationVerdictTests : IsolatedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompilationCompleteness_PreservesFailuresAndScope(bool gitScope)
    {
        if (!GitFixtureRunner.IsAvailable(out var reason))
            Assert.Inconclusive($"Git unavailable: {reason}");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(path, "\n// verdict scope\n");

        var cases = new[]
        {
            (Cancelled: true, Completed: 1, Total: 1, Expected: "compile-incomplete"),
            (Cancelled: false, Completed: 1, Total: 3, Expected: "compile-incomplete"),
            (Cancelled: false, Completed: 0, Total: 3, Expected: "compile-incomplete"),
            (Cancelled: false, Completed: 3, Total: 3, Expected: "clean"),
            (Cancelled: false, Completed: 0, Total: 0, Expected: "clean"),
        };
        foreach (var sample in cases)
        {
            foreach (var summary in new[] { false, true })
            {
                var compile = Compile(sample.Cancelled, sample.Completed, sample.Total);
                var service = CreateService(compile);
                var result = await ValidateAsync(service, workspace.WorkspaceId, path, gitScope, summary);
                Assert.AreEqual(sample.Expected, result.OverallStatus, $"{sample}, summary={summary}");
                Assert.AreEqual(0, result.ErrorCount);
                Assert.AreEqual(compile, result.CompileResult);
                CollectionAssert.AreEqual(new[] { path }, result.ChangedFilePaths.ToArray());
            }
        }

        var partial = Compile(cancelled: true, completed: 0, total: 3);
        var compilerFailure = CreateService(partial with { ErrorCount = 1 });
        Assert.AreEqual("compile-error", (await ValidateAsync(compilerFailure, workspace.WorkspaceId, path, gitScope)).OverallStatus);

        var diagnosticFailure = CreateService(partial, diagnostics: new FixedDiagnostics(
            new DiagnosticDto("CA1000", "design error", "Error", "Design", path, 1, 1, 1, 2)));
        var diagnosticResult = await ValidateAsync(diagnosticFailure, workspace.WorkspaceId, path, gitScope, summary: true);
        Assert.AreEqual("analyzer-error", diagnosticResult.OverallStatus);
        Assert.AreEqual(1, diagnosticResult.ErrorCount);
        Assert.HasCount(0, diagnosticResult.ErrorDiagnostics);

        var runnerFailure = CreateService(partial, runner: new DelegateRunner(_ => throw new InvalidOperationException("runner failed")));
        Assert.AreEqual("test-failure", (await ValidateAsync(runnerFailure, workspace.WorkspaceId, path, gitScope)).OverallStatus);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TestPhaseTimeout_IsRetryable_AndCallerCancellationStillPropagates(bool gitScope)
    {
        if (!GitFixtureRunner.IsAvailable(out var reason))
            Assert.Inconclusive($"Git unavailable: {reason}");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(path, "\n// timeout scope\n");
        var entered = false;
        var runner = new DelegateRunner(async token =>
        {
            entered = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancellation must end the runner.");
        });
        var service = CreateService(Compile(), runner, phaseTimeout: TimeSpan.FromMilliseconds(50));
        var result = await ValidateAsync(service, workspace.WorkspaceId, path, gitScope);
        Assert.IsTrue(entered, "Only the test phase may block; earlier phases return synchronously.");
        Assert.AreEqual("timeout", result.OverallStatus);
        CollectionAssert.AreEqual(new[] { path }, result.ChangedFilePaths.ToArray());
        Assert.IsNotNull(result.TestRunResult?.FailureEnvelope);
        Assert.AreEqual("Timeout", result.TestRunResult.FailureEnvelope.ErrorKind);
        Assert.IsTrue(result.TestRunResult.FailureEnvelope.IsRetryable);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("'test_run'", StringComparison.Ordinal)));

        using var cancellation = new CancellationTokenSource();
        var cancellingRunner = new DelegateRunner(token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Caller cancellation must propagate.");
        });
        var cancellingService = CreateService(Compile(), cancellingRunner);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ValidateAsync(cancellingService, workspace.WorkspaceId, path, gitScope, ct: cancellation.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Timeout_PreservesKnownAndUnknownPaths(bool gitScope)
    {
        if (!GitFixtureRunner.IsAvailable(out var reason))
            Assert.Inconclusive($"Git unavailable: {reason}");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        var unknown = workspace.GetPath("NotInWorkspace.cs");
        await File.AppendAllTextAsync(path, "\n// timeout known scope\n");
        await File.WriteAllTextAsync(unknown, "// unknown scope");
        var compile = new BlockingCompile();
        var service = CreateService(Compile(), compileService: compile, phaseTimeout: TimeSpan.FromMilliseconds(50));

        var result = gitScope
            ? await service.ValidateRecentGitChangesAsync(workspace.WorkspaceId, runTests: false, CancellationToken.None)
            : await service.ValidateAsync(workspace.WorkspaceId, [path, unknown, path], runTests: false, CancellationToken.None);

        Assert.IsTrue(compile.Entered);
        Assert.AreEqual("timeout", result.OverallStatus);
        CollectionAssert.AreEqual(new[] { path }, result.ChangedFilePaths.ToArray());
        CollectionAssert.AreEqual(new[] { unknown }, result.UnknownFilePaths.ToArray());
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("'compile_check'", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Timeout_PreservesTrackerScope(bool gitFallback, bool reconcile)
    {
        if (!GitFixtureRunner.IsAvailable(out var reason))
            Assert.Inconclusive($"Git unavailable: {reason}");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        var reverted = workspace.GetPath("SampleLib", "IAnimal.cs");
        if (reconcile)
        {
            GitFixtureRunner.InitializeRepository(workspace.RootPath);
            GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
            await File.AppendAllTextAsync(path, "\n// retained tracker scope\n");
        }

        using var tracker = new ChangeTracker(WorkspaceManager);
        tracker.RecordChange(workspace.WorkspaceId, "tracked edit", reconcile ? [path, reverted] : [path], "test");
        var compile = new BlockingCompile();
        var service = CreateService(Compile(), tracker: tracker, compileService: compile,
            phaseTimeout: TimeSpan.FromMilliseconds(50));
        var result = gitFallback
            ? await service.ValidateRecentGitChangesAsync(workspace.WorkspaceId, runTests: false, CancellationToken.None)
            : await service.ValidateAsync(workspace.WorkspaceId, changedFilePaths: null, runTests: false, CancellationToken.None);

        Assert.IsTrue(compile.Entered);
        Assert.AreEqual("timeout", result.OverallStatus);
        CollectionAssert.AreEqual(new[] { path }, result.ChangedFilePaths.ToArray());
        Assert.HasCount(0, result.UnknownFilePaths);
        if (gitFallback)
            Assert.IsTrue(result.Warnings.Count > 1, "Preserve the Git fallback warning alongside the timeout warning.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ZeroRunWarning_OnlyAccompaniesZeroRunVerdict(bool gitScope)
    {
        if (!GitFixtureRunner.IsAvailable(out var reason))
            Assert.Inconclusive($"Git unavailable: {reason}");

        await using var workspace = await CreateIsolatedWorkspaceAsync();
        GitFixtureRunner.InitializeRepository(workspace.RootPath);
        GitFixtureRunner.StageAndCommitAll(workspace.RootPath);
        var path = workspace.GetPath("SampleLib", "AnimalService.cs");
        await File.AppendAllTextAsync(path, "\n// warning scope\n");
        var empty = PassingTests() with { Total = 0, Passed = 0 };
        var cases = new (ITestRunnerService Runner, string Status)[]
        {
            (new DelegateRunner(_ => Task.FromResult(empty)), "test-zero-run"),
            (new DelegateRunner(_ => Task.FromResult(empty with { Failed = 1 })), "test-failure"),
            (new DelegateRunner(_ => throw new InvalidOperationException("runner failed")), "test-failure"),
            (new DelegateRunner(_ => Task.FromResult(PassingTests())), "clean"),
        };
        foreach (var sample in cases)
        {
            var service = CreateService(Compile(), sample.Runner);
            var result = await ValidateAsync(service, workspace.WorkspaceId, path, gitScope);
            Assert.AreEqual(sample.Status, result.OverallStatus);
            Assert.AreEqual(sample.Status == "test-zero-run" ? 1 : 0, result.Warnings.Count);
            if (sample.Status == "test-zero-run")
            {
                StringAssert.Contains(result.Warnings[0], "FullyQualifiedName=VerdictProbe");
                StringAssert.Contains(result.Warnings[0], "no tests were reported");
                Assert.IsFalse(result.Warnings[0].Contains("timing", StringComparison.Ordinal));
            }
        }
    }

    private static Task<WorkspaceValidationDto> ValidateAsync(
        WorkspaceValidationService service, string workspaceId, string path,
        bool gitScope, bool summary = false, CancellationToken ct = default) =>
        gitScope
            ? service.ValidateRecentGitChangesAsync(workspaceId, runTests: true, ct, summary)
            : service.ValidateAsync(workspaceId, [path], runTests: true, ct, summary);

    private static WorkspaceValidationService CreateService(
        CompileCheckDto compile, ITestRunnerService? runner = null,
        IDiagnosticService? diagnostics = null, TimeSpan? phaseTimeout = null,
        IChangeTracker? tracker = null, ICompileCheckService? compileService = null) =>
        new(compileService ?? new FixedCompile(compile), diagnostics ?? new FixedDiagnostics(), new FixedDiscovery(),
            runner ?? new DelegateRunner(_ => Task.FromResult(PassingTests())), WorkspaceManager,
            changeTracker: tracker, gitStatusTimeout: TimeSpan.FromSeconds(5),
            validationPhaseTimeout: phaseTimeout ?? TimeSpan.FromSeconds(5));

    private static CompileCheckDto Compile(bool cancelled = false, int completed = 1, int total = 1) =>
        new(Success: !cancelled && completed == total && total > 0, ErrorCount: 0, WarningCount: 0,
            TotalDiagnostics: 0, ReturnedDiagnostics: 0, Offset: 0, Limit: 200, HasMore: false,
            Diagnostics: [], ElapsedMs: 0, Cancelled: cancelled, CompletedProjects: completed, TotalProjects: total);

    private static TestRunResultDto PassingTests() => new(
        new CommandExecutionDto("dotnet", ["test"], string.Empty, string.Empty, 0, true, 0, string.Empty, string.Empty),
        Total: 1, Passed: 1, Failed: 0, Skipped: 0, Failures: []);

    private sealed class FixedCompile(CompileCheckDto result) : ICompileCheckService
    {
        public Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class BlockingCompile : ICompileCheckService
    {
        public bool Entered { get; private set; }

        public async Task<CompileCheckDto> CheckAsync(string workspaceId, CompileCheckOptions options, CancellationToken ct)
        {
            Entered = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Cancellation must end compilation.");
        }
    }

    private sealed class FixedDiagnostics(params DiagnosticDto[] errors) : IDiagnosticService
    {
        public Task<DiagnosticsResultDto> GetDiagnosticsAsync(string workspaceId, string? projectFilter,
            string? fileFilter, string? severityFilter, string? diagnosticIdFilter, CancellationToken ct) =>
            Task.FromResult(new DiagnosticsResultDto([], [], errors, errors.Length, 0, 0));

        public Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(string workspaceId, string diagnosticId,
            string filePath, int line, int column, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedDiscovery : ITestDiscoveryService
    {
        public Task<RelatedTestsForFilesDto> FindRelatedTestsForFilesAsync(string workspaceId,
            IReadOnlyList<string> filePaths, int maxResults, CancellationToken ct) =>
            Task.FromResult(new RelatedTestsForFilesDto([], "FullyQualifiedName=VerdictProbe",
                new PaginationInfo(0, 0, false), new RelatedTestsDiagnosticsDto(1, ["fixed test fixture"], [])));

        public Task<TestDiscoveryDto> DiscoverTestsAsync(string workspaceId, CancellationToken ct) => throw new NotSupportedException();

        public Task<RelatedTestsForSymbolDto> FindRelatedTestsAsync(string workspaceId, SymbolLocator locator,
            int maxResults, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class DelegateRunner(Func<CancellationToken, Task<TestRunResultDto>> run) : ITestRunnerService
    {
        public Task<TestRunResultDto> RunTestsAsync(string workspaceId, string? projectName, string? filter, CancellationToken ct) => run(ct);
    }
}
