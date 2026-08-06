using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for <c>workspace-eviction-no-auto-retry-on-tool-call</c>.
///
/// <para>
/// When a workspace is evicted under <see cref="WorkspaceManagerOptions.MaxConcurrentWorkspaces"/>
/// pressure, <c>compile_check</c> and <c>test_run</c> must rehydrate it from the evicted session's
/// recorded <c>LoadedPath</c> and retry once instead of surfacing a hard "workspace not found"
/// failure. The retry is opt-in by DI availability: without an <see cref="IWorkspaceManager"/> the
/// original (pre-fix) error shape must be preserved byte-for-byte.
/// </para>
///
/// <para>
/// The eviction is staged the same way the production failure occurred — a
/// <see cref="EvictPolicy.Lru"/> load against a saturated cap, which routes through
/// <c>WorkspaceManager.Close</c> and therefore records the evicted session (id, loadedAt,
/// loadedPath) that the retry path depends on. With the cap still saturated at retry time, the
/// rehydration itself must use <see cref="EvictPolicy.Lru"/>; a strict reload would fail with
/// "already tracking N workspaces" and recover nothing.
/// </para>
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceEvictionAutoRetryTests
{
    private static string s_repositoryRootPath = null!;
    private static string s_sampleSolutionPath = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        MsBuildInitializer.EnsureInitialized();
        s_repositoryRootPath = TestFixtureFileSystem.FindRepositoryRoot();
        s_sampleSolutionPath = TestFixtureFileSystem.FindFixturePath(
            s_repositoryRootPath,
            "SampleSolution",
            "SampleSolution.slnx",
            "SampleSolution.sln");
    }

    [TestMethod]
    public async Task CompileCheck_EvictedWorkspace_TransparentlyReloadsAndRetries()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var compileCheckService = new CompileCheckService(manager, NullLogger<CompileCheckService>.Instance);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            var json = await CompileCheckTools.CompileCheck(
                gate,
                compileCheckService,
                workspaceId: evictedId,
                workspaceManager: manager,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.IsFalse(root.TryGetProperty("error", out _),
                $"compile_check must transparently recover from an eviction, not return an error envelope. Actual: {json}");
            Assert.IsTrue(root.TryGetProperty("success", out _),
                $"Expected a normal compile_check payload after the transparent reload+retry. Actual: {json}");

            Assert.IsFalse(manager.ContainsWorkspace(evictedId),
                "The retry must run against a NEW workspace id, not resurrect the evicted one.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    [TestMethod]
    public async Task RunTests_EvictedWorkspace_TransparentlyReloadsAndRetries()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var runner = new RecordingTestRunnerService();

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            var json = await ValidationTools.RunTests(
                gate,
                runner,
                workspaceId: evictedId,
                projectName: null,
                filter: null,
                progress: null,
                workspaceManager: manager,
                ct: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            Assert.IsFalse(doc.RootElement.TryGetProperty("error", out _),
                $"test_run must transparently recover from an eviction, not return an error envelope. Actual: {json}");

            // The gate's ContainsWorkspace precheck rejects the evicted id before the service is
            // ever reached, so the runner sees exactly one invocation — the post-reload retry.
            Assert.AreEqual(1, runner.ObservedWorkspaceIds.Count,
                "The test runner must be invoked exactly once — on the post-reload retry leg.");
            Assert.AreNotEqual(evictedId, runner.ObservedWorkspaceIds[0],
                "The retry must target the rehydrated workspace id, not the evicted one.");
            Assert.IsTrue(manager.ContainsWorkspace(runner.ObservedWorkspaceIds[0]),
                "The id handed to the retry leg must be a live session.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    [TestMethod]
    public async Task CompileCheck_EvictedWorkspace_NoWorkspaceManagerSupplied_PreservesOriginalNotFoundEnvelope()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var compileCheckService = new CompileCheckService(manager, NullLogger<CompileCheckService>.Instance);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            var json = await ToolExecutionTestHarness.RunAsync(
                "compile_check",
                () => CompileCheckTools.CompileCheck(
                    gate,
                    compileCheckService,
                    workspaceId: evictedId,
                    ct: CancellationToken.None));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.IsTrue(root.TryGetProperty("error", out var errorProp) && errorProp.GetBoolean(),
                $"Without an IWorkspaceManager the pre-fix error envelope must survive unchanged. Actual: {json}");
            Assert.AreEqual("NotFound", root.GetProperty("category").GetString(),
                $"The gate's ContainsWorkspace precheck classifies as NotFound; the retry wiring must not change that when unwired. Actual: {json}");
            Assert.AreEqual("compile_check", root.GetProperty("tool").GetString());
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    [TestMethod]
    public async Task RunTests_EvictedWorkspace_NoWorkspaceManagerSupplied_StillPropagatesLookupMiss()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var runner = new RecordingTestRunnerService();

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            // Pre-fix behaviour: the gate precheck's KeyNotFoundException escapes test_run's
            // inline envelope formatting and reaches the global filter as IsError=true. Pinning
            // it here so the retry wiring cannot silently downgrade a hard failure to content.
            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
                () => ValidationTools.RunTests(
                    gate,
                    runner,
                    workspaceId: evictedId,
                    projectName: null,
                    filter: null,
                    progress: null,
                    ct: CancellationToken.None));

            Assert.AreEqual(0, runner.ObservedWorkspaceIds.Count,
                "With no manager wired the service must never be reached.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// Non-recovering branch 1 of 3 — <b>genuinely never-loaded id, manager wired</b>.
    ///
    /// <para>
    /// Combination pinned: gate <c>ContainsWorkspace</c> precheck miss (plain
    /// <see cref="KeyNotFoundException"/>, NOT <see cref="WorkspaceEvictedException"/>) ×
    /// <c>IWorkspaceManager</c> supplied × no eviction record for the id. The re-probe in
    /// <c>ToolDispatch.TryReclassifyAsEvicted</c> therefore falls into its
    /// <c>catch (KeyNotFoundException) return null</c> arm, so no rehydration load is ever
    /// attempted and the original lookup miss propagates exactly as it did before the retry
    /// wiring existed.
    /// </para>
    ///
    /// <para>
    /// A live session is loaded first so the manager's active-session count is non-zero — that
    /// keeps <c>GetRequiredSession</c> off its cross-process host-recycle branch, which would
    /// otherwise reclassify a never-loaded id as evicted whenever an unrelated test left a
    /// recycle signal in the process-wide <see cref="WorkspaceEvictionRegistry"/>.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task RunTests_NeverLoadedWorkspaceId_ManagerWired_AttemptsNoReloadAndPropagatesLookupMiss()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var runner = new RecordingTestRunnerService();

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var liveStatus = await manager.LoadAsync(path1, EvictPolicy.Strict, CancellationToken.None);
            var bogusId = $"never-loaded-{Guid.NewGuid():N}";

            // Unit-level pin on the branch itself: a never-loaded id yields no eviction record,
            // so the reclassification probe returns null and the retry leg is skipped outright.
            Assert.IsNull(
                ToolDispatch.TryReclassifyAsEvicted(manager, bogusId),
                "A never-loaded id carries no eviction record; the reclassification probe must return null.");

            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
                () => ValidationTools.RunTests(
                    gate,
                    runner,
                    workspaceId: bogusId,
                    projectName: null,
                    filter: null,
                    progress: null,
                    workspaceManager: manager,
                    ct: CancellationToken.None));

            Assert.AreEqual(0, runner.ObservedWorkspaceIds.Count,
                "No reload is possible for a never-loaded id, so the runner must never be reached.");
            Assert.IsTrue(manager.ContainsWorkspace(liveStatus.WorkspaceId),
                "No rehydration load may be attempted — the unrelated live session must not be LRU-evicted to make room.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
        }
    }

    /// <summary>
    /// Non-recovering branch 2 of 3 — <b>eviction record present, rehydration reload fails</b>.
    ///
    /// <para>
    /// Combination pinned: gate <c>ContainsWorkspace</c> precheck miss (plain
    /// <see cref="KeyNotFoundException"/>) × <c>IWorkspaceManager</c> supplied × a real eviction
    /// record whose recorded <c>LoadedPath</c> no longer exists on disk. The reclassification
    /// probe succeeds (so the retry leg <i>is</i> entered — the contrast with the never-loaded
    /// case above), but <c>WorkspaceManager.LoadAsync</c> throws
    /// <see cref="FileNotFoundException"/> from its path validation, which
    /// <c>ToolDispatch.TryReloadEvictedWorkspaceForRetryAsync</c> swallows and reports as
    /// <see langword="null"/>. The original lookup miss must then propagate unchanged rather
    /// than being replaced by the reload's own (less informative) failure.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task RunTests_EvictedWorkspace_RehydrationReloadFails_PropagatesOriginalLookupMiss()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var runner = new RecordingTestRunnerService();

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            // Fixture precondition AND the discriminator against the never-loaded case: this id
            // DOES reclassify as evicted with a recoverable path, so the reload leg is entered.
            var reclassified = ToolDispatch.TryReclassifyAsEvicted(manager, evictedId);
            Assert.IsNotNull(reclassified,
                "Fixture precondition: the LRU-evicted id must reclassify as an eviction.");
            Assert.AreEqual(Path.GetFullPath(path1), reclassified.LoadedPath,
                "Fixture precondition: the eviction record must carry the original solution path.");

            // Make the recorded LoadedPath unloadable so the rehydration attempt fails.
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);

            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
                () => ValidationTools.RunTests(
                    gate,
                    runner,
                    workspaceId: evictedId,
                    projectName: null,
                    filter: null,
                    progress: null,
                    workspaceManager: manager,
                    ct: CancellationToken.None));

            Assert.AreEqual(0, runner.ObservedWorkspaceIds.Count,
                "A failed rehydration must not run the suite against some other session.");

            // Unit-level pin on the swallow-and-report-null arm the orchestrator depends on.
            var reloadResult = await ToolDispatch.TryReloadEvictedWorkspaceForRetryAsync(
                manager,
                evictedId,
                new KeyNotFoundException("probe"),
                CancellationToken.None);
            Assert.IsNull(reloadResult,
                "A reload that throws must be reported as an unrecoverable retry, not surfaced to the caller.");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// Non-recovering branch 3 of 3 — <b>mid-call eviction with no recoverable path</b>.
    ///
    /// <para>
    /// Combination pinned: the gate precheck <i>passes</i> (the workspace is live) and the
    /// <see cref="WorkspaceEvictedException"/> is raised by the deeper service lookup, so it
    /// travels through <c>RunTestsOnceAsync</c>'s deliberate
    /// <c>catch (WorkspaceEvictedException) { throw; }</c> instead of its inline formatter.
    /// An <c>IWorkspaceManager</c> <i>is</i> supplied, but the eviction carries no recorded
    /// <c>LoadedPath</c> (the cross-process host-recycle shape), so
    /// <c>TryReloadEvictedWorkspaceForRetryAsync</c> returns <see langword="null"/> without
    /// attempting a load — leaving the orchestrator to reproduce the pre-existing inline
    /// <c>ReportStage("done")</c> → <c>ClassifyAndFormat</c> → <c>InjectSchemaHintIfPossible</c>
    /// envelope. That envelope is asserted byte-for-byte against the same three calls made
    /// directly on the very exception instance the fake threw.
    /// </para>
    ///
    /// <para>
    /// <b>Test double, not a real race:</b> the eviction is injected by an
    /// <see cref="ITestRunnerService"/> that throws on invocation. That exercises the code path
    /// the genuine "evicted strictly between the precheck and the deeper lookup" timing window
    /// reaches; it does not reproduce the window itself. The result is deliberately compared
    /// raw rather than through <c>ToolExecutionTestHarness</c>, whose <c>_meta</c> injection
    /// carries a nondeterministic elapsed-ms field that byte-for-byte equality cannot tolerate.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task RunTests_MidCallEviction_UnrecoverableRetry_ReproducesInlineEnvelopeByteForByte()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);

        // 3-arg ctor == cross-process recycle shape: LoadedPath is null, so there is nothing to
        // rehydrate from and the retry bails before any load is attempted.
        var midCallEviction = new WorkspaceEvictedException(
            "ws-mid-call",
            new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero),
            "Workspace 'ws-mid-call' was evicted while the call was in flight.");
        var runner = new EvictingTestRunnerService(midCallEviction);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            // Live workspace — the gate precheck must PASS so the eviction is observed mid-call.
            var liveStatus = await manager.LoadAsync(path1, EvictPolicy.Strict, CancellationToken.None);

            var json = await ValidationTools.RunTests(
                gate,
                runner,
                workspaceId: liveStatus.WorkspaceId,
                projectName: null,
                filter: null,
                progress: null,
                workspaceManager: manager,
                ct: CancellationToken.None);

            Assert.AreEqual(1, runner.InvocationCount,
                "The suite must be attempted exactly once — an unrecoverable eviction must not re-run it.");

            var expected = ToolErrorHandler.InjectSchemaHintIfPossible(
                ToolErrorHandler.ClassifyAndFormat(midCallEviction, "test_run"),
                "test_run");
            Assert.AreEqual(expected, json,
                "The unrecoverable mid-call eviction must reproduce the pre-existing inline envelope byte-for-byte.");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.IsTrue(root.TryGetProperty("error", out var errorProp) && errorProp.GetBoolean(),
                $"Expected an error envelope, not a test-run payload. Actual: {json}");
            Assert.AreEqual("WorkspaceEvicted", root.GetProperty("category").GetString(),
                $"The eviction category must survive the retry wiring. Actual: {json}");
            Assert.AreEqual("test_run", root.GetProperty("tool").GetString());
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
        }
    }

    /// <summary>
    /// <c>workspace-eviction-retry-swallowed-log</c> — a failed rehydration reload must now emit
    /// a Warning-or-higher log entry (via the optional <see cref="ILoggerFactory"/>) referencing
    /// the evicted workspace id, in addition to the pre-existing byte-for-byte-preserved fallback
    /// behaviour (the original NotFound envelope is unchanged).
    /// </summary>
    [TestMethod]
    public async Task CompileCheck_EvictedWorkspace_RehydrationReloadFails_LogsWarning()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var compileCheckService = new CompileCheckService(manager, NullLogger<CompileCheckService>.Instance);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            // Make the recorded LoadedPath unloadable so the rehydration attempt fails.
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);

            var json = await ToolExecutionTestHarness.RunAsync(
                "compile_check",
                () => CompileCheckTools.CompileCheck(
                    gate,
                    compileCheckService,
                    workspaceId: evictedId,
                    workspaceManager: manager,
                    loggerFactory: loggerFactory,
                    ct: CancellationToken.None));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.IsTrue(root.TryGetProperty("error", out var errorProp) && errorProp.GetBoolean(),
                $"A failed reload must still surface the original NotFound envelope unchanged. Actual: {json}");
            Assert.AreEqual("NotFound", root.GetProperty("category").GetString(),
                $"Fallback behaviour must be byte-for-byte preserved — only a log emission is added. Actual: {json}");

            Assert.AreEqual(1,
                logger.Entries.Count(e =>
                    e.Level >= LogLevel.Warning && e.Message.Contains(evictedId, StringComparison.Ordinal)),
                $"Expected exactly one Warning-or-higher log entry referencing the evicted workspace id. Entries: {string.Join(" | ", logger.Entries.Select(e => $"[{e.Level}] {e.Message}"))}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// <c>workspace-eviction-retry-swallowed-log</c> — the <c>test_run</c> analogue of
    /// <see cref="CompileCheck_EvictedWorkspace_RehydrationReloadFails_LogsWarning"/>: a failed
    /// rehydration reload must log a Warning-or-higher entry while the original
    /// <see cref="KeyNotFoundException"/> still propagates unchanged.
    /// </summary>
    [TestMethod]
    public async Task RunTests_EvictedWorkspace_RehydrationReloadFails_LogsWarning()
    {
        using var manager = CreateManager(maxConcurrentWorkspaces: 1);
        var gate = new WorkspaceExecutionGate(new ExecutionGateOptions(), manager);
        var runner = new RecordingTestRunnerService();
        var logger = new RecordingLogger();
        var loggerFactory = new RecordingLoggerFactory(logger);

        var path1 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);
        var path2 = TestFixtureFileSystem.CreateSampleSolutionCopy(s_repositoryRootPath, s_sampleSolutionPath);

        try
        {
            var evictedId = await StageEvictionAsync(manager, path1, path2);

            // Make the recorded LoadedPath unloadable so the rehydration attempt fails.
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);

            await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
                () => ValidationTools.RunTests(
                    gate,
                    runner,
                    workspaceId: evictedId,
                    projectName: null,
                    filter: null,
                    progress: null,
                    workspaceManager: manager,
                    loggerFactory: loggerFactory,
                    ct: CancellationToken.None));

            Assert.AreEqual(0, runner.ObservedWorkspaceIds.Count,
                "A failed rehydration must not run the suite against some other session.");

            Assert.AreEqual(1,
                logger.Entries.Count(e =>
                    e.Level >= LogLevel.Warning && e.Message.Contains(evictedId, StringComparison.Ordinal)),
                $"Expected exactly one Warning-or-higher log entry referencing the evicted workspace id. Entries: {string.Join(" | ", logger.Entries.Select(e => $"[{e.Level}] {e.Message}"))}");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path1)!);
            TestFixtureFileSystem.DeleteDirectoryIfExists(Path.GetDirectoryName(path2)!);
        }
    }

    /// <summary>
    /// Loads <paramref name="path1"/> into the (cap-saturated) manager, then LRU-loads
    /// <paramref name="path2"/> so the first session is evicted with a recoverable eviction
    /// record. Returns the now-evicted workspace id.
    /// </summary>
    private static async Task<string> StageEvictionAsync(WorkspaceManager manager, string path1, string path2)
    {
        var status1 = await manager.LoadAsync(path1, EvictPolicy.Strict, CancellationToken.None);
        await manager.LoadAsync(path2, EvictPolicy.Lru, CancellationToken.None);

        Assert.IsFalse(manager.ContainsWorkspace(status1.WorkspaceId),
            "Fixture precondition: the first workspace must have been LRU-evicted.");

        return status1.WorkspaceId;
    }

    private static WorkspaceManager CreateManager(int maxConcurrentWorkspaces)
    {
        return new WorkspaceManager(
            NullLogger<WorkspaceManager>.Instance,
            new PreviewStore(),
            new FileWatcherService(NullLogger<FileWatcherService>.Instance),
            new WorkspaceManagerOptions { MaxConcurrentWorkspaces = maxConcurrentWorkspaces });
    }

    /// <summary>
    /// Records the workspace id each invocation was routed to and returns a canned successful
    /// run, so the test can assert the retry leg targeted the rehydrated session.
    /// </summary>
    private sealed class RecordingTestRunnerService : ITestRunnerService
    {
        public List<string> ObservedWorkspaceIds { get; } = [];

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId,
            string? projectName,
            string? filter,
            CancellationToken ct)
        {
            ObservedWorkspaceIds.Add(workspaceId);
            return Task.FromResult(new TestRunResultDto(
                Execution: new CommandExecutionDto(
                    Command: "dotnet",
                    Arguments: ["test"],
                    WorkingDirectory: "C:/fake",
                    TargetPath: "C:/fake/proj.csproj",
                    ExitCode: 0,
                    Succeeded: true,
                    DurationMs: 1,
                    StdOut: "Passed!",
                    StdErr: string.Empty),
                Total: 1,
                Passed: 1,
                Failed: 0,
                Skipped: 0,
                Failures: []));
        }
    }

    /// <summary>
    /// Throws a caller-supplied <see cref="WorkspaceEvictedException"/> on every invocation,
    /// simulating the deeper <c>GetRequiredSession</c> lookup evicting strictly after the gate's
    /// <c>ContainsWorkspace</c> precheck passed. Counts invocations so the test can prove the
    /// unrecoverable path does not silently re-run the suite.
    /// </summary>
    private sealed class EvictingTestRunnerService : ITestRunnerService
    {
        private readonly WorkspaceEvictedException _eviction;

        public EvictingTestRunnerService(WorkspaceEvictedException eviction) => _eviction = eviction;

        public int InvocationCount { get; private set; }

        public Task<TestRunResultDto> RunTestsAsync(
            string workspaceId,
            string? projectName,
            string? filter,
            CancellationToken ct)
        {
            InvocationCount++;
            throw _eviction;
        }
    }

    /// <summary>
    /// Copied from <c>WorkspaceCloseDrainTests.cs</c> — a minimal <see cref="ILoggerFactory"/>
    /// that always resolves to the single <see cref="RecordingLogger"/> it wraps, so the test can
    /// assert on emitted log entries regardless of the category name the caller requests.
    /// </summary>
    private sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
