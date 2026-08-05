using System.Text.Json;
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
}
