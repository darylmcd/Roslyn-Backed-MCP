using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for <c>workspace-restore-budget</c>: the <c>autoRestore</c> phase must run under its
/// own <see cref="ValidationServiceOptions.RestoreTimeout"/>, clamped to the enclosing request
/// deadline minus <see cref="ValidationServiceOptions.RestoreReloadReserve"/>, so a slow restore
/// cannot consume the whole gate deadline and starve the follow-up reload. Recording
/// <see cref="IGatedCommandExecutor"/> and a fake clock; no real <c>dotnet restore</c> runs.
/// </summary>
[DoNotParallelize]
[TestClass]
public sealed class WorkspaceLoadRestoreBudgetTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [TestMethod]
    public async Task Restore_WithoutAmbientDeadline_UsesRestoreTimeoutAlone()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(90) };

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            AssertNodeBudgets(status, executor, TimeSpan.FromSeconds(90));
        });
    }

    [TestMethod]
    public async Task Restore_AmbientDeadlineTighterThanRestoreTimeout_ClampsToRemainingMinusReserve()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions
            {
                RestoreTimeout = TimeSpan.FromSeconds(90),
                RestoreReloadReserve = TimeSpan.FromSeconds(30),
            };
            var clock = new FakeTimeProvider();
            using var deadline = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(100));
            clock.Advance(TimeSpan.FromSeconds(10)); // 90s of the request left -> 60s for restore

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            AssertNodeBudgets(status, executor, TimeSpan.FromSeconds(60));
        });
    }

    [TestMethod]
    public async Task Restore_AmbientDeadlineLooserThanRestoreTimeout_KeepsRestoreTimeout()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(45) };
            using var deadline = RequestDeadline.Begin(new FakeTimeProvider(), TimeSpan.FromMinutes(10));

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System);

            AssertNodeBudgets(status, executor, TimeSpan.FromSeconds(45));
        });
    }

    [TestMethod]
    public async Task Restore_NoBudgetLeftAfterReserve_ThrowsPathFreeTimeoutAndNeverSpawnsRestore()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreReloadReserve = TimeSpan.FromSeconds(30) };
            using var deadline = RequestDeadline.Begin(new FakeTimeProvider(), TimeSpan.FromSeconds(20));

            var thrown = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System));

            Assert.AreEqual(0, executor.Timeouts.Count, "no dotnet restore may be spawned when no budget remains.");
            StringAssert.Contains(thrown.Message, "ROSLYNMCP_RESTORE_TIMEOUT_SECONDS");
            StringAssert.Contains(thrown.Message, "ROSLYNMCP_REQUEST_TIMEOUT_SECONDS");
            Assert.IsFalse(
                thrown.Message.Contains(status.LoadedPath!, StringComparison.OrdinalIgnoreCase),
                $"Message leaked the project path: {thrown.Message}");
        });
    }

    [TestMethod]
    public async Task Restore_ZeroRestoreTimeout_ThrowsTimeoutAndNeverSpawnsRestore()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.Zero };

            await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System));

            Assert.AreEqual(0, executor.Timeouts.Count);
        });
    }

    [TestMethod]
    public async Task Restore_CallerCancelled_PropagatesOperationCanceledNotTimeout()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                    executor, new ValidationServiceOptions(), manager, status, autoRestore: true, NullLogger.Instance, cts.Token, timeProvider:System.TimeProvider.System));
        });
    }

    [TestMethod]
    public async Task Restore_GateNestedWriteInsideLoadGate_HonorsTheEarlierLoadDeadline()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions
            {
                RestoreTimeout = TimeSpan.FromMinutes(10),
                RestoreReloadReserve = TimeSpan.FromSeconds(30),
            };
            var clock = new FakeTimeProvider();
            var gate = new WorkspaceExecutionGate(
                new ExecutionGateOptions { RequestTimeout = TimeSpan.FromSeconds(120) },
                manager,
                clock);

            // Mirrors workspace_reload: write gate nested inside the load gate. The write gate
            // arms its own fresh 120s, but the load gate's earlier deadline must still win.
            await gate.RunLoadGateAsync(outerCt =>
                gate.RunWriteAsync(status.WorkspaceId, async innerCt =>
                {
                    clock.Advance(TimeSpan.FromSeconds(50)); // 70s left on the load deadline
                    await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                        executor, options, manager, status, autoRestore: true, NullLogger.Instance, innerCt, timeProvider:System.TimeProvider.System);
                    return 0;
                }, outerCt, applyStalenessPolicy: false), CancellationToken.None);

            AssertNodeBudgets(status, executor, TimeSpan.FromSeconds(40));
        });
    }

    [TestMethod]
    public async Task Restore_MultiProjectCommands_CannotResetTheAmbientDeadlineAfterFirstCommand()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            Assert.IsGreaterThan(1, status.ProjectCount);
            var clock = new FakeTimeProvider();
            using var deadline = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(100));
            var executor = new RecordingExecutor { AfterExecution = () => clock.Advance(TimeSpan.FromSeconds(80)) };
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromMinutes(5), RestoreReloadReserve = TimeSpan.FromSeconds(30) };
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None, timeProvider:System.TimeProvider.System));
            Assert.HasCount(1, executor.Timeouts);
            AssertBudgetNear(TimeSpan.FromSeconds(70), executor.Timeouts[0]);
        });
    }

    private static void AssertNodeBudgets(WorkspaceStatusDto status, RecordingExecutor executor, TimeSpan expected)
    {
        Assert.HasCount(status.ProjectCount, executor.Timeouts);
        foreach (var budget in executor.Timeouts) AssertBudgetNear(expected, budget);
        for (var index = 1; index < executor.Timeouts.Count; index++)
            Assert.IsTrue(executor.Timeouts[index] <= executor.Timeouts[index - 1], "A later project must consume the same phase's remaining budget.");
    }

    private static void AssertBudgetNear(TimeSpan expected, TimeSpan actual)
    {
        // These callers use TimeProvider.System, so allow a little elapsed wall time.
        Assert.IsTrue(
            actual <= expected && actual >= expected - TimeSpan.FromSeconds(5),
            $"Expected a budget just under {expected}, got {actual}.");
    }

    private async Task WithRestoreRequiredWorkspace(Func<WorkspaceManager, WorkspaceStatusDto, Task> body)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
        try
        {
            using var manager = new WorkspaceManager(new RoslynMcp.Roslyn.Services.ForkInvocationPathPolicy(new RoslynMcp.Tests.Helpers.OwnedTestWorkspacePathPolicy()),
                NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(),
                new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });
            var status = await manager.LoadAsync(copiedSolutionPath, RoslynMcp.Tests.Helpers.OwnedNativeLoadContext.Properties(copiedSolutionPath, new Dictionary<string, string>()), CancellationToken.None);
            await body(manager, status with { RestoreRequired = true });
        }
        finally
        {
            DeleteDirectoryIfExists(copiedRoot);
        }
    }

    /// <summary>Records each invocation's timeout and succeeds; honors a cancelled token like the real executor.</summary>
    private sealed class RecordingExecutor : IGatedCommandExecutor
    {
        public Task<BuildCommandRun> ExecuteBuildAsync(BuildCommandPlan plan, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException("This fixture does not execute builds.");

        public async Task<CommandExecutionDto> ExecuteAuthorizedAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
            Func<CancellationToken, Task> authorizeStart, CancellationToken ct)
        {
            await authorizeStart(ct).ConfigureAwait(false);
            return await ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct).ConfigureAwait(false);
        }
        public Task<TestRunResultDto> ExecuteTestAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments,
            Func<CommandExecutionDto, CancellationToken, Task<TestRunResultDto>> collectResults,
            TimeSpan timeout, IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, Func<CancellationToken, Task> authorizeStart, CancellationToken ct) =>
            throw new NotSupportedException("This fixture does not execute tests.");

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId, string targetPath, IReadOnlyList<string> arguments, TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns, CancellationToken ct)
        {
            if (earlyKillPatterns is { Count: > 0 })
                throw new NotSupportedException("This fixture does not simulate streaming early-kill patterns.");
            return ExecuteAsync(workspaceId, targetPath, arguments, timeout, ct);
        }

        public List<TimeSpan> Timeouts { get; } = [];
        public Action? AfterExecution { get; init; }

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Timeouts.Add(timeout);
            AfterExecution?.Invoke();
            return Task.FromResult(new CommandExecutionDto(
                "dotnet", arguments, Path.GetDirectoryName(targetPath)!, targetPath, 0, true, 0, string.Empty, string.Empty));
        }

        public ProjectStatusDto ResolveProject(string workspaceId, string projectName) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
