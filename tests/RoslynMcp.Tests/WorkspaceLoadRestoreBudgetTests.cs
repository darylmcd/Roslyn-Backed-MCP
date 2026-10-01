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

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task Restore_WithoutAmbientDeadline_UsesRestoreTimeoutAlone()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(90) };

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, CancellationToken.None);

            Assert.AreEqual(1, executor.Timeouts.Count);
            AssertBudgetNear(TimeSpan.FromSeconds(90), executor.Timeouts[0]);
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
                executor, options, manager, status, autoRestore: true, CancellationToken.None);

            AssertBudgetNear(TimeSpan.FromSeconds(60), executor.Timeouts.Single());
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
                executor, options, manager, status, autoRestore: true, CancellationToken.None);

            AssertBudgetNear(TimeSpan.FromSeconds(45), executor.Timeouts.Single());
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
                    executor, options, manager, status, autoRestore: true, CancellationToken.None));

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
                    executor, options, manager, status, autoRestore: true, CancellationToken.None));

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
                    executor, new ValidationServiceOptions(), manager, status, autoRestore: true, cts.Token));
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
                        executor, options, manager, status, autoRestore: true, innerCt);
                    return 0;
                }, outerCt, applyStalenessPolicy: false), CancellationToken.None);

            AssertBudgetNear(TimeSpan.FromSeconds(40), executor.Timeouts.Single());
        });
    }

    private static void AssertBudgetNear(TimeSpan expected, TimeSpan actual)
    {
        // The phase clock is a real Stopwatch, so allow a little elapsed wall time.
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
            using var manager = new WorkspaceManager(
                NullLogger<WorkspaceManager>.Instance,
                new PreviewStore(),
                new FileWatcherService(NullLogger<FileWatcherService>.Instance),
                new WorkspaceManagerOptions { MaxConcurrentWorkspaces = 4, RestoreRaceWaitMs = 0 });
            var status = await manager.LoadAsync(copiedSolutionPath, CancellationToken.None);
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
        public List<TimeSpan> Timeouts { get; } = [];

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Timeouts.Add(timeout);
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
