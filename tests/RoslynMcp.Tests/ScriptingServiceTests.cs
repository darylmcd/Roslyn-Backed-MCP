using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScriptingServiceTests
{
    private static readonly TimeSpan ContendedCompletionTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [Timeout(10_000)]
    public async Task EvaluateAsync_FiniteNonCooperativeScript_ReturnsHardDeadlineAndWorkerExits()
    {
        var options = new ScriptingServiceOptions
        {
            WatchdogGraceSeconds = 0,
            HeartbeatIntervalMs = 100,
            MaxAbandonedEvaluations = 1,
        };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);

        // Warm the Roslyn scripting runtime so a cold compilation cannot consume the entire
        // one-second budget before the deliberately non-cooperative statement begins.
        var warmup = await service.EvaluateAsync(
            "0",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 30).ConfigureAwait(false);
        Assert.IsTrue(warmup.Success, warmup.Error);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.EvaluateAsync(
            "System.Threading.Thread.Sleep(2500); 42",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 1).ConfigureAwait(false);
        sw.Stop();

        Assert.IsFalse(result.Success, "A non-cooperative script should hit the hard deadline.");
        Assert.IsFalse(string.IsNullOrEmpty(result.Error), "Expected error message.");
        StringAssert.Contains(result.Error!, "forcibly abandoned");
        Assert.AreEqual(1, result.AppliedScriptTimeoutSeconds);
        Assert.IsTrue(sw.Elapsed.TotalSeconds < 5, $"Expected sub-5s completion, took {sw.Elapsed.TotalSeconds:F1}s");
        Assert.AreEqual(0, service.ActiveEvaluationCount, "The completed request should release its capacity slot.");

        Assert.IsTrue(
            SpinWait.SpinUntil(() => service.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(4)),
            "The finite worker should exit and leave no abandoned thread in the test host.");

        var recovered = await service.EvaluateAsync(
            "20 + 22",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);
        Assert.IsTrue(recovered.Success, recovered.Error);
        Assert.AreEqual("42", recovered.ResultValue);
        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task EvaluateAsync_AbandonedWorkerCap_ProjectsFailureAndRecoversAfterWorkerExit()
    {
        var options = new ScriptingServiceOptions
        {
            MaxAbandonedEvaluations = 1,
        };
        using var worker = new ControlledWorker();
        var service = new ScriptingService(
            NullLogger<ScriptingService>.Instance,
            options,
            (code, _, cancellationToken) =>
                string.Equals(code, "block", StringComparison.Ordinal)
                    ? worker.RunSynchronously(cancellationToken)
                    : ScriptExecutionOutcome.Success(42));
        using var cancellation = new CancellationTokenSource();

        try
        {
            var execution = service.EvaluateAsync(
                "block",
                imports: null,
                cancellation.Token,
                onProgress: null,
                timeoutSecondsOverride: 30);
            Assert.IsTrue(worker.WaitUntilEntered(), "The controlled worker should enter before cancellation.");

            // Caller cancellation deterministically abandons this non-cooperative worker. The
            // separate finite-script test owns the production deadline-timer integration path.
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                await execution.WaitAsync(ContendedCompletionTimeout).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.AreEqual(1, service.AbandonedEvaluationCount);
            Assert.AreEqual(0, service.ActiveEvaluationCount);

            var capacityResult = await service.EvaluateAsync(
                "recover",
                imports: null,
                CancellationToken.None,
                onProgress: null,
                timeoutSecondsOverride: 5).ConfigureAwait(false);
            Assert.IsFalse(
                capacityResult.Success,
                "The public service should reject work while its abandoned-worker cap is full.");
            StringAssert.Contains(capacityResult.Error!, "abandoned worker threads");
            Assert.AreEqual(
                0,
                capacityResult.ElapsedMs,
                "The public capacity guard should fail before starting another script worker.");
        }
        finally
        {
            cancellation.Cancel();
            worker.ReleaseAndWait();
        }

        Assert.IsTrue(
            SpinWait.SpinUntil(() => service.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(2)),
            "The released worker should drain the public abandoned-worker count.");

        var recovered = await service.EvaluateAsync(
            "recover",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);
        Assert.IsTrue(recovered.Success, recovered.Error);
        Assert.AreEqual("42", recovered.ResultValue);
        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExecuteAsync_AbandonedWorkerCap_FailsFastAndRecoversAfterWorkersExit()
    {
        var options = new ScriptingServiceOptions
        {
            MaxConcurrentEvaluations = 1,
            MaxAbandonedEvaluations = 2,
        };
        var supervisor = new ScriptExecutionSupervisor(NullLogger<ScriptingService>.Instance, options);
        using var firstWorker = new ControlledWorker();
        using var secondWorker = new ControlledWorker();
        var startedWorkers = new List<ControlledWorker>();

        try
        {
            startedWorkers.Add(firstWorker);
            var first = await ExecuteUntilImmediateDeadlineAsync(supervisor, firstWorker).ConfigureAwait(false);
            Assert.AreEqual(ScriptExecutionOutcomeKind.HardDeadline, first.Outcome.Kind);
            Assert.AreEqual(1, supervisor.AbandonedEvaluationCount);
            Assert.AreEqual(0, supervisor.ActiveEvaluationCount, "A hard deadline should release the capacity slot.");

            // MaxConcurrentEvaluations is one, so reaching a second worker while the first is
            // still blocked proves that hard-deadline completion released the first slot.
            startedWorkers.Add(secondWorker);
            var second = await ExecuteUntilImmediateDeadlineAsync(supervisor, secondWorker).ConfigureAwait(false);
            Assert.AreEqual(ScriptExecutionOutcomeKind.HardDeadline, second.Outcome.Kind);
            Assert.AreEqual(2, supervisor.AbandonedEvaluationCount);

            var unexpectedWorkerStarted = 0;
            var capacityResult = await supervisor.ExecuteAsync(
                _ =>
                {
                    Interlocked.Exchange(ref unexpectedWorkerStarted, 1);
                    return ScriptExecutionOutcome.Success(99);
                },
                onProgress: null,
                CreateSuccessfulExecutionSettings(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(capacityResult.CapacityFailure.HasValue);
            Assert.AreEqual(
                ScriptExecutionCapacityFailureKind.AbandonedWorkerCap,
                capacityResult.CapacityFailure.Value.Kind);
            Assert.AreEqual(ScriptExecutionOutcomeKind.CapacityFailure, capacityResult.Outcome.Kind);
            Assert.AreEqual(0, capacityResult.ElapsedMs, "The abandoned-worker cap should fail before starting a worker.");
            Assert.AreEqual(0, unexpectedWorkerStarted, "A capacity failure must not invoke the worker callback.");
            Assert.AreEqual(0, supervisor.ActiveEvaluationCount);
        }
        finally
        {
            foreach (var worker in startedWorkers)
            {
                worker.ReleaseAndWait();
            }
        }

        Assert.IsTrue(
            SpinWait.SpinUntil(() => supervisor.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(2)),
            "Released workers should drain the abandoned-worker count.");

        var recovered = await supervisor.ExecuteAsync(
            _ => ScriptExecutionOutcome.Success(15),
            onProgress: null,
            CreateSuccessfulExecutionSettings(),
            CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual(ScriptExecutionOutcomeKind.Success, recovered.Outcome.Kind);
        Assert.AreEqual(15, recovered.Outcome.Result);
        Assert.AreEqual(0, supervisor.ActiveEvaluationCount);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExecuteAsync_OuterCancellation_ReleasesCapacityAndDrainsAfterWorkerReturns()
    {
        var options = new ScriptingServiceOptions
        {
            MaxConcurrentEvaluations = 1,
            MaxAbandonedEvaluations = 2,
        };
        var supervisor = new ScriptExecutionSupervisor(NullLogger<ScriptingService>.Instance, options);
        using var worker = new ControlledWorker();
        using var cts = new CancellationTokenSource();
        var execution = worker.ExecuteAsync(
            supervisor,
            CreateLongExecutionSettings(),
            cts.Token);

        try
        {
            Assert.IsTrue(worker.WaitUntilEntered(), "The worker should start before outer cancellation.");
            cts.Cancel();

            var result = await execution.WaitAsync(ContendedCompletionTimeout).ConfigureAwait(false);
            Assert.AreEqual(ScriptExecutionOutcomeKind.OuterCancelled, result.Outcome.Kind);
            Assert.AreEqual(1, supervisor.AbandonedEvaluationCount, "An externally-cancelled blocked worker should be tracked.");
            Assert.AreEqual(0, supervisor.ActiveEvaluationCount, "Outer cancellation should release the capacity slot.");
        }
        finally
        {
            cts.Cancel();
            worker.ReleaseAndWait();
        }

        Assert.IsTrue(
            SpinWait.SpinUntil(() => supervisor.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(2)),
            "The released worker must not survive the test.");
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExecuteAsync_CancellationAfterWorkerStarts_CannotBecomeTimeoutOutcome()
    {
        var supervisor = new ScriptExecutionSupervisor(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { MaxConcurrentEvaluations = 1 });
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();

        var execution = supervisor.ExecuteAsync(
            timeoutToken =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(4));
                timeoutToken.ThrowIfCancellationRequested();
                return ScriptExecutionOutcome.Success(42);
            },
            onProgress: null,
            CreateLongExecutionSettings(),
            cts.Token);

        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(2)), "Worker did not reach the cancellation window.");
        cts.Cancel();
        release.Set();

        var result = await execution.WaitAsync(ContendedCompletionTimeout).ConfigureAwait(false);
        Assert.AreEqual(
            ScriptExecutionOutcomeKind.OuterCancelled,
            result.Outcome.Kind,
            "Caller cancellation must win over the linked worker token's timeout-shaped cancellation.");
        Assert.IsTrue(
            SpinWait.SpinUntil(() => supervisor.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(2)),
            "The cooperative worker should drain after caller cancellation.");
        Assert.AreEqual(0, supervisor.ActiveEvaluationCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_UnexpectedWorkerException_ReturnsRuntimeOutcomeAndReleasesCapacity()
    {
        var supervisor = new ScriptExecutionSupervisor(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { MaxConcurrentEvaluations = 1 });
        var expected = new InvalidOperationException("worker boundary sentinel");

        var result = await supervisor.ExecuteAsync(
            _ => throw expected,
            onProgress: null,
            CreateSuccessfulExecutionSettings(),
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(ScriptExecutionOutcomeKind.Runtime, result.Outcome.Kind);
        Assert.AreSame(expected, result.Outcome.RuntimeException);
        Assert.AreEqual(0, supervisor.ActiveEvaluationCount);
        Assert.AreEqual(0, supervisor.AbandonedEvaluationCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_OutOfRangeTimerSettings_FailBeforeWorkerOrCapacityAcquisition()
    {
        var supervisor = new ScriptExecutionSupervisor(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { MaxConcurrentEvaluations = 1 });
        var workerStarted = 0;
        var invalidSettings = new ScriptExecutionSupervisorSettings(
            EffectiveTimeoutSeconds: int.MaxValue,
            GraceSeconds: 0,
            HardDeadlineSeconds: int.MaxValue,
            HeartbeatInterval: TimeSpan.FromMilliseconds(100),
            Budget: TimeSpan.FromSeconds(int.MaxValue));

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await supervisor.ExecuteAsync(
                _ =>
                {
                    Interlocked.Exchange(ref workerStarted, 1);
                    return ScriptExecutionOutcome.Success(42);
                },
                onProgress: null,
                invalidSettings,
                CancellationToken.None));

        Assert.AreEqual(0, workerStarted, "Invalid timers must fail before user code starts.");
        Assert.AreEqual(0, supervisor.ActiveEvaluationCount, "Invalid timers must not consume a capacity slot.");
        Assert.AreEqual(0, supervisor.AbandonedEvaluationCount, "No worker exists to abandon for invalid settings.");
    }

    [TestMethod]
    public async Task EvaluateAsync_TimeoutBeyondTimerRange_FailsBeforeStartingEvaluation()
    {
        var service = new ScriptingService(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { WatchdogGraceSeconds = 10 });

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.EvaluateAsync(
                "while (true) { }",
                imports: null,
                CancellationToken.None,
                onProgress: null,
                timeoutSecondsOverride: ScriptingServiceOptions.MaxTimerDurationSeconds));

        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task EvaluateAsync_NonPositiveExplicitTimeout_IsRejectedAtServiceBoundary(int timeoutSeconds)
    {
        var service = new ScriptingService(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions());

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await service.EvaluateAsync(
                "42",
                imports: null,
                CancellationToken.None,
                onProgress: null,
                timeoutSecondsOverride: timeoutSeconds));

        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task EvaluateAsync_LongRunningAsyncScript_ReportsHeartbeatProgress()
    {
        var options = new ScriptingServiceOptions
        {
            HeartbeatIntervalMs = 100,
            WatchdogGraceSeconds = 1,
        };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);
        var observedHeartbeats = 0;

        var result = await service.EvaluateAsync(
            "await Task.Delay(500); 123",
            imports: null,
            CancellationToken.None,
            _ => Interlocked.Increment(ref observedHeartbeats),
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual("123", result.ResultValue);
        Assert.IsTrue(result.ProgressHeartbeatCount is >= 1, "Expected the DTO to report at least one heartbeat.");
        Assert.IsTrue(observedHeartbeats >= 1, "Expected the progress sink to receive at least one heartbeat.");
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task EvaluateAsync_CooperativeBudgetCancellation_ReturnsTimeoutInsteadOfRuntimeError()
    {
        var service = new ScriptingService(
            NullLogger<ScriptingService>.Instance,
            new ScriptingServiceOptions { WatchdogGraceSeconds = 2 },
            (code, scriptOptions, timeoutToken) =>
            {
                timeoutToken.WaitHandle.WaitOne();
                return ScriptingService.ExecuteScript(code, scriptOptions, timeoutToken);
            });

        var result = await service.EvaluateAsync(
            "42",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 1).ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error ?? string.Empty, "timed out after 1 seconds");
        Assert.IsFalse(
            result.Error?.Contains("Runtime error", StringComparison.Ordinal) ?? false,
            "Cooperative budget cancellation must be classified at the supervisor boundary.");
        Assert.AreEqual(0, service.ActiveEvaluationCount);
        Assert.AreEqual(0, service.AbandonedEvaluationCount);
    }

    [TestMethod]
    [Timeout(25_000)]
    public async Task ExecuteAsync_ThrowingProgressCallback_DoesNotTerminateEvaluation()
    {
        var logger = new ScriptLogCounter();
        var supervisor = new ScriptExecutionSupervisor(
            logger,
            new ScriptingServiceOptions { HeartbeatIntervalMs = 20, StuckWarningSeconds = 0 });
        var callbackCount = 0;
        var callbackEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWorker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = supervisor.ExecuteAsync(
            _ =>
            {
                releaseWorker.Task.GetAwaiter().GetResult();
                return ScriptExecutionOutcome.Success(42);
            },
            _ =>
            {
                Interlocked.Increment(ref callbackCount);
                callbackEntered.TrySetResult(true);
                try
                {
                    releaseCallback.Task.GetAwaiter().GetResult();
                    throw new InvalidOperationException("progress sink failure");
                }
                finally
                {
                    callbackExited.TrySetResult(true);
                }
            },
            new ScriptExecutionSupervisorSettings(
                EffectiveTimeoutSeconds: 30,
                GraceSeconds: 0,
                HardDeadlineSeconds: 30,
                HeartbeatInterval: TimeSpan.FromMilliseconds(20),
                Budget: TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        var signalTimeout = ContendedCompletionTimeout;
        bool progressStarted;
        bool overlappingHeartbeatObserved;
        bool failureSignalsObserved;
        ScriptExecutionResult result;
        try
        {
            progressStarted = await CompletesWithinAsync(callbackEntered.Task, signalTimeout).ConfigureAwait(false);
            // The first callback is blocked above the slow-warning branch. Observing that warning
            // therefore proves a later timer invocation overlapped and continued internal accounting.
            overlappingHeartbeatObserved = progressStarted &&
                await CompletesWithinAsync(logger.SlowWarningObserved, signalTimeout).ConfigureAwait(false);
            releaseCallback.TrySetResult(true);
            failureSignalsObserved = progressStarted &&
                await CompletesWithinAsync(
                    Task.WhenAll(callbackExited.Task, logger.ProgressFailureWarningObserved),
                    signalTimeout).ConfigureAwait(false);
            releaseWorker.TrySetResult(true);
            result = await execution.WaitAsync(signalTimeout).ConfigureAwait(false);
        }
        finally
        {
            releaseCallback.TrySetResult(true);
            releaseWorker.TrySetResult(true);
        }

        Assert.IsTrue(progressStarted, "The first progress callback should start while the worker is blocked.");
        Assert.IsTrue(
            overlappingHeartbeatObserved,
            "A later heartbeat should run while the first progress callback remains blocked.");
        Assert.IsTrue(
            failureSignalsObserved,
            "The failing callback should exit and log its failure before the worker completes.");
        Assert.AreEqual(ScriptExecutionOutcomeKind.Success, result.Outcome.Kind);
        Assert.AreEqual(42, result.Outcome.Result);
        Assert.AreEqual(1, callbackCount, "A failing progress sink should be disabled after its first exception.");
        Assert.IsTrue(
            result.HeartbeatCount >= 2,
            "Internal heartbeat accounting should continue while overlapping progress delivery is suppressed.");
        Assert.AreEqual(1, logger.ProgressFailureWarnings);
        Assert.AreEqual(1, logger.SlowWarnings, "Overlapping timer callbacks must emit the slow warning once.");
        Assert.AreEqual(0, supervisor.ActiveEvaluationCount);
    }

    private static async Task<bool> CompletesWithinAsync(Task signal, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(signal, Task.Delay(timeout)).ConfigureAwait(false);
        return completed == signal;
    }

    [TestMethod]
    public async Task EvaluateAsync_SimpleExpression_ReturnsResult()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "21 + 34",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual("55", result.ResultValue);
    }

    [TestMethod]
    public async Task EvaluateAsync_RuntimeError_ReturnsCaughtError()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "throw new System.InvalidOperationException(\"boom\");",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Error?.Contains("InvalidOperationException", StringComparison.Ordinal), result.Error);
        StringAssert.Contains(result.Error!, "boom");
    }

    [TestMethod]
    public async Task EvaluateAsync_CompilationError_ReturnsDiagnostics()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, new ScriptingServiceOptions());
        var result = await service.EvaluateAsync(
            "this is not valid C#",
            imports: null,
            CancellationToken.None,
            onProgress: null,
            timeoutSecondsOverride: 5).ConfigureAwait(false);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.CompilationErrors);
        Assert.IsTrue(result.CompilationErrors!.Count > 0);
        Assert.IsTrue(result.CompilationErrors.All(diagnostic => diagnostic.Location is null),
            "Script diagnostics have coordinates but no resolvable file path, so nested locations must remain null.");
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task EvaluateAsync_OuterCancellation_PropagatesPromptly()
    {
        var options = new ScriptingServiceOptions { WatchdogGraceSeconds = 30 };
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance, options);
        using var cts = new CancellationTokenSource();
        var evaluation = service.EvaluateAsync(
            "await System.Threading.Tasks.Task.Delay(2000); 42",
            imports: null,
            cts.Token,
            onProgress: null,
            timeoutSecondsOverride: 30);

        try
        {
            Assert.IsTrue(
                SpinWait.SpinUntil(() => service.ActiveEvaluationCount == 1, TimeSpan.FromSeconds(2)),
                "The evaluation should acquire its capacity slot before cancellation.");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            cts.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                await evaluation.ConfigureAwait(false)).ConfigureAwait(false);
            sw.Stop();

            Assert.IsTrue(sw.Elapsed.TotalSeconds < 3, $"Outer cancellation should propagate quickly, took {sw.Elapsed.TotalSeconds:F1}s");
            Assert.AreEqual(0, service.ActiveEvaluationCount, "Outer cancellation should release the capacity slot.");
        }
        finally
        {
            cts.Cancel();
            try
            {
                await evaluation.WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected public result after outer cancellation. The worker drains separately below.
            }

            Assert.IsTrue(
                SpinWait.SpinUntil(() => service.AbandonedEvaluationCount == 0, TimeSpan.FromSeconds(4)),
                "The finite script worker should exit instead of surviving in the test host.");
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteUntilImmediateDeadlineAsync(
        ScriptExecutionSupervisor supervisor,
        ControlledWorker worker)
    {
        var execution = worker.ExecuteAsync(
            supervisor,
            CreateImmediateDeadlineSettings(),
            CancellationToken.None);
        Assert.IsTrue(worker.WaitUntilEntered(), "The controlled worker should enter before assertions continue.");
        return await execution.WaitAsync(ContendedCompletionTimeout).ConfigureAwait(false);
    }

    private static ScriptExecutionSupervisorSettings CreateImmediateDeadlineSettings() =>
        new(
            EffectiveTimeoutSeconds: 1,
            GraceSeconds: 0,
            HardDeadlineSeconds: 0,
            HeartbeatInterval: TimeSpan.FromMilliseconds(100),
            Budget: TimeSpan.FromSeconds(1));

    private static ScriptExecutionSupervisorSettings CreateSuccessfulExecutionSettings() =>
        new(
            EffectiveTimeoutSeconds: 2,
            GraceSeconds: 0,
            HardDeadlineSeconds: 2,
            HeartbeatInterval: TimeSpan.FromMilliseconds(100),
            Budget: TimeSpan.FromSeconds(2));

    private static ScriptExecutionSupervisorSettings CreateLongExecutionSettings() =>
        new(
            EffectiveTimeoutSeconds: 30,
            GraceSeconds: 0,
            HardDeadlineSeconds: 30,
            HeartbeatInterval: TimeSpan.FromMilliseconds(100),
            Budget: TimeSpan.FromSeconds(30));

    private sealed class ControlledWorker : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private readonly ManualResetEventSlim _exited = new(false);
        private int _scheduled;

        public Task<ScriptExecutionResult> ExecuteAsync(
            ScriptExecutionSupervisor supervisor,
            ScriptExecutionSupervisorSettings settings,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(0, Interlocked.Exchange(ref _scheduled, 1), "A controlled worker can only be scheduled once.");
            return supervisor.ExecuteAsync(
                Run,
                onProgress: null,
                settings,
                cancellationToken);
        }

        public bool WaitUntilEntered() => _entered.Wait(TimeSpan.FromSeconds(2));

        public ScriptExecutionOutcome RunSynchronously(CancellationToken cancellationToken)
        {
            Assert.AreEqual(
                0,
                Interlocked.Exchange(ref _scheduled, 1),
                "A controlled worker can only be scheduled once.");
            return Run(cancellationToken);
        }

        public void ReleaseAndWait()
        {
            _release.Set();
            Assert.IsTrue(
                _exited.Wait(TimeSpan.FromSeconds(4)),
                "The controlled worker should exit after its release signal.");
        }

        public void Dispose()
        {
            _release.Set();
            if (Volatile.Read(ref _scheduled) != 0 && !_exited.Wait(TimeSpan.FromSeconds(4)))
            {
                // Keep the events alive if the test has already failed and a worker is still using them.
                return;
            }

            _entered.Dispose();
            _release.Dispose();
            _exited.Dispose();
        }

        private ScriptExecutionOutcome Run(CancellationToken _)
        {
            _entered.Set();
            try
            {
                _release.Wait();
                return ScriptExecutionOutcome.Success(42);
            }
            finally
            {
                _exited.Set();
            }
        }
    }

    private sealed class ScriptLogCounter : ILogger
    {
        private int _progressFailureWarnings;
        private int _slowWarnings;
        private readonly TaskCompletionSource<bool> _progressFailureWarningObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _slowWarningObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProgressFailureWarnings => Volatile.Read(ref _progressFailureWarnings);
        public int SlowWarnings => Volatile.Read(ref _slowWarnings);
        public Task ProgressFailureWarningObserved => _progressFailureWarningObserved.Task;
        public Task SlowWarningObserved => _slowWarningObserved.Task;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains("progress callback failed", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _progressFailureWarnings);
                _progressFailureWarningObserved.TrySetResult(true);
            }

            if (message.Contains("still running after", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _slowWarnings);
                _slowWarningObserved.TrySetResult(true);
            }
        }
    }
}
