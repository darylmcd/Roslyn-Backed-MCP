using System.Diagnostics;
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
// Each test owns its copied solution, manager and recording executor; request deadlines
// are scoped to the async flow, so concurrent classes do not share budget state.
[TestClass]
public sealed class WorkspaceLoadRestoreBudgetTests : SharedWorkspaceTestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    [TestMethod]
    public async Task Restore_WithoutAmbientDeadline_UsesRemainingRestorePhaseBudget()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(90) };

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None);

            Assert.AreEqual(1, executor.Timeouts.Count);
            AssertBudgetWithinObservedPhase(options.RestoreTimeout, null, executor.ElapsedAtEntry[0], executor.Timeouts[0]);
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
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None);

            AssertBudgetWithinObservedPhase(options.RestoreTimeout, TimeSpan.FromSeconds(60), executor.ElapsedAtEntry.Single(), executor.Timeouts.Single());
        });
    }

    [TestMethod]
    public async Task Restore_AmbientDeadlineLooserThanRestoreTimeout_UsesRemainingRestorePhaseBudget()
    {
        await WithRestoreRequiredWorkspace(async (manager, status) =>
        {
            var executor = new RecordingExecutor();
            var options = new ValidationServiceOptions { RestoreTimeout = TimeSpan.FromSeconds(45) };
            using var deadline = RequestDeadline.Begin(new FakeTimeProvider(), TimeSpan.FromMinutes(10));

            await WorkspaceTools.RestoreAndReloadIfRequiredAsync(
                executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None);

            AssertBudgetWithinObservedPhase(options.RestoreTimeout, TimeSpan.FromSeconds(570), executor.ElapsedAtEntry.Single(), executor.Timeouts.Single());
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
                    executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None));

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
                    executor, options, manager, status, autoRestore: true, NullLogger.Instance, CancellationToken.None));

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
                    executor, new ValidationServiceOptions(), manager, status, autoRestore: true, NullLogger.Instance, cts.Token));
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
                        executor, options, manager, status, autoRestore: true, NullLogger.Instance, innerCt);
                    return 0;
                }, outerCt, applyStalenessPolicy: false), CancellationToken.None);

            AssertBudgetWithinObservedPhase(options.RestoreTimeout, TimeSpan.FromSeconds(40), executor.ElapsedAtEntry.Single(), executor.Timeouts.Single());
        });
    }

    [TestMethod]
    [DataRow(45, 570, 6, 39)]
    [DataRow(90, 60, 6, 60)]
    public void BudgetObservation_ValidPhaseElapsed_Passes(int timeoutSeconds, int ambientSeconds, int elapsedSeconds, int actualSeconds)
    {
        AssertBudgetWithinObservedPhase(
            TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(ambientSeconds),
            TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(actualSeconds));
    }

    [TestMethod]
    [DataRow(45, 570, 6, 38)]
    [DataRow(45, 570, 6, 46)]
    [DataRow(45, 570, 6, 0)]
    [DataRow(45, 570, 6, -1)]
    [DataRow(90, 60, 6, 59)]
    [DataRow(90, 60, 6, 61)]
    public void BudgetObservation_OutsideMeasuredBounds_Fails(int timeoutSeconds, int ambientSeconds, int elapsedSeconds, int actualSeconds)
    {
        Assert.ThrowsExactly<AssertFailedException>(() => AssertBudgetWithinObservedPhase(
            TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(ambientSeconds),
            TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(actualSeconds)));
    }

    [TestMethod]
    public void BudgetObservation_WithoutAmbientDeadline_AllowsMeasuredPhaseElapsed()
    {
        AssertBudgetWithinObservedPhase(
            TimeSpan.FromSeconds(45), null, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(39));
    }

    [TestMethod]
    public void RemainingBudget_WithoutAmbientDeadline_SubtractsPhaseElapsed()
    {
        var actual = RestoreInvocationPlanner.RemainingBudget(
            TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30), "budget exhausted");

        Assert.AreEqual(TimeSpan.FromSeconds(39), actual);
    }

    [TestMethod]
    [DataRow(45, 6, 600, 0, 39)]
    [DataRow(90, 6, 100, 10, 60)]
    [DataRow(90, 31, 100, 10, 59)]
    public void RemainingBudget_WithAmbientDeadline_UsesTighterRemainingBudget(
        int timeoutSeconds, int elapsedSeconds, int requestSeconds, int requestElapsedSeconds, int expectedSeconds)
    {
        var clock = new FakeTimeProvider();
        using var deadline = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(requestSeconds));
        clock.Advance(TimeSpan.FromSeconds(requestElapsedSeconds));

        var actual = RestoreInvocationPlanner.RemainingBudget(
            TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(elapsedSeconds),
            TimeSpan.FromSeconds(30), "budget exhausted");

        Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [TestMethod]
    public void RemainingBudget_NestedDeadline_HonorsEarlierEnclosingDeadline()
    {
        var clock = new FakeTimeProvider();
        using var outer = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(120));
        clock.Advance(TimeSpan.FromSeconds(50));
        using var inner = RequestDeadline.Begin(clock, TimeSpan.FromSeconds(120));

        var actual = RestoreInvocationPlanner.RemainingBudget(
            TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30), "budget exhausted");

        Assert.AreEqual(TimeSpan.FromSeconds(40), actual);
    }

    [TestMethod]
    [DataRow(45, 45)]
    [DataRow(45, 46)]
    public void RemainingBudget_ExhaustedPhase_ThrowsSuppliedTimeout(int timeoutSeconds, int elapsedSeconds)
    {
        const string message = "phase budget exhausted";

        var thrown = Assert.ThrowsExactly<TimeoutException>(() => RestoreInvocationPlanner.RemainingBudget(
            TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(30), message));

        Assert.AreEqual(message, thrown.Message);
    }

    [TestMethod]
    [DataRow(20)]
    [DataRow(30)]
    public void RemainingBudget_ExhaustedAmbientAfterReserve_ThrowsSuppliedTimeout(int requestSeconds)
    {
        const string message = "ambient budget exhausted";
        using var deadline = RequestDeadline.Begin(new FakeTimeProvider(), TimeSpan.FromSeconds(requestSeconds));

        var thrown = Assert.ThrowsExactly<TimeoutException>(() => RestoreInvocationPlanner.RemainingBudget(
            TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(30), message));

        Assert.AreEqual(message, thrown.Message);
    }

    private static void AssertBudgetWithinObservedPhase(
        TimeSpan restoreTimeout, TimeSpan? ambientLimit, TimeSpan elapsedUpperBound, TimeSpan actual)
    {
        // The recorder starts before the production phase and samples at executor entry, so its
        // elapsed time encloses all phase work charged to this invocation's timeout.
        var minimum = restoreTimeout - elapsedUpperBound;
        var maximum = restoreTimeout;
        if (ambientLimit is { } ambient)
        {
            minimum = ambient < minimum ? ambient : minimum;
            maximum = ambient < maximum ? ambient : maximum;
        }

        Assert.IsTrue(actual > TimeSpan.Zero, $"Expected a positive restore budget, got {actual}.");
        Assert.IsTrue(
            actual >= minimum && actual <= maximum,
            $"Expected a restore budget in [{minimum}, {maximum}] for observed elapsed {elapsedUpperBound}, got {actual}.");
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
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();

        public List<TimeSpan> Timeouts { get; } = [];
        public List<TimeSpan> ElapsedAtEntry { get; } = [];

        public Task<CommandExecutionDto> ExecuteAsync(
            string workspaceId,
            string targetPath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            IReadOnlyList<EarlyKillPattern>? earlyKillPatterns,
            CancellationToken ct)
        {
            ElapsedAtEntry.Add(_elapsed.Elapsed);
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
