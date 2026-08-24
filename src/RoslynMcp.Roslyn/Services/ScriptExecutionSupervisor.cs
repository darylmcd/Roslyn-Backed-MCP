using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

internal sealed class ScriptExecutionSupervisor
{
    private readonly ILogger _logger;
    private readonly ScriptingServiceOptions _options;
    private readonly SemaphoreSlim _concurrencyGate;
    private long _activeEvaluations;
    private long _abandonedEvaluations;

    public ScriptExecutionSupervisor(ILogger logger, ScriptingServiceOptions options)
    {
        _logger = logger;
        _options = options;
        var maxConcurrent = Math.Max(1, options.MaxConcurrentEvaluations);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    internal long ActiveEvaluationCount => Interlocked.Read(ref _activeEvaluations);
    internal long AbandonedEvaluationCount => Interlocked.Read(ref _abandonedEvaluations);

    public async Task<ScriptExecutionResult> ExecuteAsync(
        Func<CancellationToken, ScriptExecutionOutcome> executeWorker,
        Action<ScriptEvaluationProgress>? onProgress,
        ScriptExecutionSupervisorSettings settings,
        CancellationToken ct)
    {
        ValidateSettings(settings);
        var capacityFailure = await TryAcquireCapacityAsync(ct).ConfigureAwait(false);
        if (capacityFailure.HasValue)
        {
            return ScriptExecutionResult.ForCapacityFailure(capacityFailure.Value, settings);
        }

        var execution = CreateExecutionContext();
        Interlocked.Increment(ref _activeEvaluations);

        try
        {
            using var timeoutCts = CreateTimeoutTokenSource(ct, settings.Budget);
            execution.DeadlineTimer = CreateDeadlineTimer(execution.Completion);
            execution.HeartbeatTimer = CreateHeartbeatTimer(
                onProgress,
                execution.Stopwatch,
                settings,
                execution.RunState);

            using var ctRegistration = RegisterOuterCancellation(ct, execution.Completion);
            ct.ThrowIfCancellationRequested();

            var workerThread = StartWorkerThread(
                executeWorker,
                timeoutCts.Token,
                ct,
                execution.Completion,
                execution.RunState);
            try
            {
                ArmTimers(execution, settings);
            }
            catch
            {
                MarkAbandonedIfWorkerStillRunning(timeoutCts, execution.RunState);
                throw;
            }

            var raceOutcome = await execution.Completion.Task.ConfigureAwait(false);
            execution.Stopwatch.Stop();
            return FinalizeExecution(raceOutcome, execution, workerThread.Name, settings, timeoutCts);
        }
        finally
        {
            DisposeTimers(execution);
            ReleaseConcurrencySlot(execution.RunState);
        }
    }

    private static void ValidateSettings(ScriptExecutionSupervisorSettings settings)
    {
        var maximumTimerDuration = TimeSpan.FromSeconds(ScriptingServiceOptions.MaxTimerDurationSeconds);
        if (settings.EffectiveTimeoutSeconds < 1 ||
            settings.Budget <= TimeSpan.Zero ||
            settings.Budget > maximumTimerDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Script budget must be positive and within the runtime timer range.");
        }

        if (settings.HardDeadlineSeconds < 0 ||
            settings.HardDeadlineSeconds > ScriptingServiceOptions.MaxTimerDurationSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Hard deadline must be non-negative and within the runtime timer range.");
        }

        if (settings.HeartbeatInterval <= TimeSpan.Zero ||
            settings.HeartbeatInterval > maximumTimerDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Heartbeat interval must be positive and within the runtime timer range.");
        }
    }

    private async Task<ScriptExecutionCapacityFailure?> TryAcquireCapacityAsync(CancellationToken ct)
    {
        var abandonedAtEntry = Interlocked.Read(ref _abandonedEvaluations);
        if (abandonedAtEntry >= _options.MaxAbandonedEvaluations)
        {
            return new ScriptExecutionCapacityFailure(
                ScriptExecutionCapacityFailureKind.AbandonedWorkerCap,
                AbandonedCount: abandonedAtEntry,
                ActiveCount: 0);
        }

        var slotAcquireTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.ConcurrencySlotAcquireTimeoutSeconds));
        if (!await _concurrencyGate.WaitAsync(slotAcquireTimeout, ct).ConfigureAwait(false))
        {
            return new ScriptExecutionCapacityFailure(
                ScriptExecutionCapacityFailureKind.ConcurrentSlotCap,
                AbandonedCount: abandonedAtEntry,
                ActiveCount: Interlocked.Read(ref _activeEvaluations));
        }

        return null;
    }

    private static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken ct, TimeSpan budget)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(budget);
        return timeoutCts;
    }

    private static ScriptExecutionContext CreateExecutionContext() =>
        new(Stopwatch.StartNew(), new ScriptRunState(), CreateCompletionSource());

    private ScriptExecutionResult FinalizeExecution(
        ScriptExecutionOutcome raceOutcome,
        ScriptExecutionContext execution,
        string? workerThreadName,
        ScriptExecutionSupervisorSettings settings,
        CancellationTokenSource timeoutCts)
    {
        if (raceOutcome.Kind == ScriptExecutionOutcomeKind.OuterCancelled)
        {
            MarkAbandonedIfWorkerStillRunning(timeoutCts, execution.RunState);
        }
        else if (raceOutcome.Kind == ScriptExecutionOutcomeKind.HardDeadline &&
                 MarkAbandonedIfWorkerStillRunning(timeoutCts, execution.RunState))
        {
            LogHardDeadlineCritical(workerThreadName, settings);
        }

        return ScriptExecutionResult.ForOutcome(
            raceOutcome,
            execution.Stopwatch.ElapsedMilliseconds,
            execution.RunState.HeartbeatCount,
            Interlocked.Read(ref _abandonedEvaluations),
            _options.MaxAbandonedEvaluations,
            settings);
    }

    private void ReleaseConcurrencySlot(ScriptRunState runState)
    {
        if (Interlocked.Exchange(ref runState.SlotReleased, 1) != 0)
        {
            return;
        }

        Interlocked.Decrement(ref _activeEvaluations);
        try { _concurrencyGate.Release(); }
        catch (ObjectDisposedException) { /* host shutting down */ }
        catch (SemaphoreFullException) { /* defensive */ }
    }

    private static void DisposeTimers(ScriptExecutionContext execution)
    {
        try { execution.HeartbeatTimer?.Dispose(); } catch { /* best-effort */ }
        try { execution.DeadlineTimer?.Dispose(); } catch { /* best-effort */ }
    }

    private static TaskCompletionSource<ScriptExecutionOutcome> CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static CancellationTokenRegistration RegisterOuterCancellation(
        CancellationToken ct,
        TaskCompletionSource<ScriptExecutionOutcome> completion)
    {
        return ct.Register(static state =>
        {
            var tcs = (TaskCompletionSource<ScriptExecutionOutcome>)state!;
            tcs.TrySetResult(ScriptExecutionOutcome.OuterCancelled());
        }, completion);
    }

    private Thread StartWorkerThread(
        Func<CancellationToken, ScriptExecutionOutcome> executeWorker,
        CancellationToken timeoutToken,
        CancellationToken outerCancellationToken,
        TaskCompletionSource<ScriptExecutionOutcome> completion,
        ScriptRunState runState)
    {
        var workerThreadName = $"roslyn-mcp.script.{Guid.NewGuid():N}";
        if (workerThreadName.Length > 32)
        {
            workerThreadName = workerThreadName[..32];
        }

        var workerThread = new Thread(() =>
        {
            ScriptExecutionOutcome outcome;
            try
            {
                outcome = executeWorker(timeoutToken);
            }
            catch (OperationCanceledException) when (outerCancellationToken.IsCancellationRequested)
            {
                outcome = ScriptExecutionOutcome.OuterCancelled();
            }
            catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
            {
                outcome = ScriptExecutionOutcome.TimedOut();
            }
            catch (Exception ex)
            {
                outcome = ScriptExecutionOutcome.Runtime(ex);
            }

            CompleteWorkerThread(completion, outcome, runState);
        })
        {
            IsBackground = true,
            Name = workerThreadName,
        };
        workerThread.Start();
        return workerThread;
    }

    private void CompleteWorkerThread(
        TaskCompletionSource<ScriptExecutionOutcome> completion,
        ScriptExecutionOutcome outcome,
        ScriptRunState runState)
    {
        var previousState = Interlocked.CompareExchange(
            ref runState.WorkerState,
            ScriptRunState.WorkerFinished,
            ScriptRunState.WorkerRunning);
        if (previousState == ScriptRunState.WorkerRunning)
        {
            completion.TrySetResult(outcome);
            return;
        }

        if (previousState == ScriptRunState.WorkerAbandoned)
        {
            Interlocked.Decrement(ref _abandonedEvaluations);
        }
    }

    private static Timer CreateDeadlineTimer(TaskCompletionSource<ScriptExecutionOutcome> completion)
    {
        return new Timer(
            _ => completion.TrySetResult(ScriptExecutionOutcome.HardDeadline()),
            state: null,
            dueTime: Timeout.InfiniteTimeSpan,
            period: Timeout.InfiniteTimeSpan);
    }

    private Timer CreateHeartbeatTimer(
        Action<ScriptEvaluationProgress>? onProgress,
        Stopwatch sw,
        ScriptExecutionSupervisorSettings settings,
        ScriptRunState runState)
    {
        return new Timer(
            _ => EmitHeartbeat(onProgress, sw, settings, runState),
            state: null,
            dueTime: Timeout.InfiniteTimeSpan,
            period: Timeout.InfiniteTimeSpan);
    }

    private static void ArmTimers(
        ScriptExecutionContext execution,
        ScriptExecutionSupervisorSettings settings)
    {
        _ = execution.DeadlineTimer!.Change(
            TimeSpan.FromSeconds(settings.HardDeadlineSeconds),
            Timeout.InfiniteTimeSpan);
        _ = execution.HeartbeatTimer!.Change(
            settings.HeartbeatInterval,
            settings.HeartbeatInterval);
    }

    private void EmitHeartbeat(
        Action<ScriptEvaluationProgress>? onProgress,
        Stopwatch sw,
        ScriptExecutionSupervisorSettings settings,
        ScriptRunState runState)
    {
        var index = Interlocked.Increment(ref runState.HeartbeatCount);
        var elapsed = sw.Elapsed;
        if (onProgress is not null &&
            Interlocked.CompareExchange(
                ref runState.ProgressCallbackState,
                ScriptRunState.ProgressCallbackClaimedOrDisabled,
                ScriptRunState.ProgressCallbackReady) == ScriptRunState.ProgressCallbackReady)
        {
            try
            {
                onProgress(new ScriptEvaluationProgress(elapsed, settings.Budget, index));
                Volatile.Write(ref runState.ProgressCallbackState, ScriptRunState.ProgressCallbackReady);
            }
            catch (Exception ex)
            {
                // Leave the claimed state in place so no later timer callback can invoke the
                // failing sink. The CAS above also prevents overlapping progress delivery.
                _logger.LogWarning(
                    ex,
                    "evaluate_csharp: progress callback failed; disabling further heartbeat callbacks for this evaluation.");
            }
        }

        if (index == 1)
        {
            _logger.LogInformation(
                "evaluate_csharp: Roslyn script evaluation in progress (budget {BudgetSeconds}s, heartbeat every {HeartbeatMs}ms)",
                settings.EffectiveTimeoutSeconds,
                _options.HeartbeatIntervalMs);
        }

        if (elapsed.TotalSeconds < _options.StuckWarningSeconds ||
            Interlocked.CompareExchange(ref runState.SlowWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        _logger.LogWarning(
            "evaluate_csharp: still running after {ElapsedSeconds:F1}s - clients often show a static \"Evaluating C#\" step here; " +
            "large compile or synchronous script work may continue until timeout ({BudgetSeconds}s).",
            elapsed.TotalSeconds,
            settings.EffectiveTimeoutSeconds);
    }

    private bool MarkAbandonedIfWorkerStillRunning(
        CancellationTokenSource timeoutCts,
        ScriptRunState runState)
    {
        if (Interlocked.CompareExchange(
                ref runState.WorkerState,
                ScriptRunState.WorkerAbandoned,
                ScriptRunState.WorkerRunning) != ScriptRunState.WorkerRunning)
        {
            return false;
        }

        Interlocked.Increment(ref _abandonedEvaluations);
        // Approved race: the timeout CTS may already be disposed if the worker completed
        // between the hard-deadline check and this cancel. Cancellation is then a no-op and
        // the ObjectDisposedException is expected, so swallowing it here is intentional.
        // (On the HardDeadline path the caller logs LogHardDeadlineCritical right after this
        // returns true; the abandonment is already counted via _abandonedEvaluations above, so
        // no observability is lost by swallowing the cancel race.)
        try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    private void LogHardDeadlineCritical(string? workerThreadName, ScriptExecutionSupervisorSettings settings)
    {
        _logger.LogCritical(
            "evaluate_csharp WATCHDOG: hard deadline ({HardDeadlineSeconds}s = budget {BudgetSeconds}s + grace {GraceSeconds}s) " +
            "elapsed; abandoning script worker thread '{ThreadName}'. The thread continues until it returns or the host exits. " +
            "Active evaluations: {Active}, abandoned: {Abandoned} (cap {AbandonedCap}).",
            settings.HardDeadlineSeconds,
            settings.EffectiveTimeoutSeconds,
            settings.GraceSeconds,
            workerThreadName,
            Interlocked.Read(ref _activeEvaluations),
            Interlocked.Read(ref _abandonedEvaluations),
            _options.MaxAbandonedEvaluations);
    }

    private sealed class ScriptRunState
    {
        public const int WorkerRunning = 0;
        public const int WorkerFinished = 1;
        public const int WorkerAbandoned = 2;
        public const int ProgressCallbackReady = 0;
        public const int ProgressCallbackClaimedOrDisabled = 1;

        public int HeartbeatCount;
        public int ProgressCallbackState;
        public int WorkerState;
        public int SlowWarningEmitted;
        public long SlotReleased;
    }

    private sealed class ScriptExecutionContext(
        Stopwatch stopwatch,
        ScriptRunState runState,
        TaskCompletionSource<ScriptExecutionOutcome> completion)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public ScriptRunState RunState { get; } = runState;
        public TaskCompletionSource<ScriptExecutionOutcome> Completion { get; } = completion;
        public Timer? HeartbeatTimer { get; set; }
        public Timer? DeadlineTimer { get; set; }
    }
}

internal readonly record struct ScriptExecutionSupervisorSettings(
    int EffectiveTimeoutSeconds,
    int GraceSeconds,
    int HardDeadlineSeconds,
    TimeSpan HeartbeatInterval,
    TimeSpan Budget);

internal enum ScriptExecutionCapacityFailureKind
{
    AbandonedWorkerCap,
    ConcurrentSlotCap,
}

internal readonly record struct ScriptExecutionCapacityFailure(
    ScriptExecutionCapacityFailureKind Kind,
    long AbandonedCount,
    long ActiveCount);

internal sealed record ScriptExecutionResult(
    ScriptExecutionCapacityFailure? CapacityFailure,
    ScriptExecutionOutcome Outcome,
    long ElapsedMs,
    int HeartbeatCount,
    long AbandonedCount,
    int MaxAbandonedCount,
    ScriptExecutionSupervisorSettings Settings)
{
    public static ScriptExecutionResult ForCapacityFailure(
        ScriptExecutionCapacityFailure capacityFailure,
        ScriptExecutionSupervisorSettings settings) =>
        new(
            capacityFailure,
            ScriptExecutionOutcome.CapacityFailure(),
            ElapsedMs: 0,
            HeartbeatCount: 0,
            AbandonedCount: capacityFailure.AbandonedCount,
            MaxAbandonedCount: 0,
            settings);

    public static ScriptExecutionResult ForOutcome(
        ScriptExecutionOutcome outcome,
        long elapsedMs,
        int heartbeatCount,
        long abandonedCount,
        int maxAbandonedCount,
        ScriptExecutionSupervisorSettings settings) =>
        new(null, outcome, elapsedMs, heartbeatCount, abandonedCount, maxAbandonedCount, settings);
}

internal enum ScriptExecutionOutcomeKind
{
    Success,
    CompilationFailure,
    Runtime,
    TimedOut,
    HardDeadline,
    OuterCancelled,
    CapacityFailure,
}

internal readonly struct ScriptExecutionOutcome
{
    public ScriptExecutionOutcomeKind Kind { get; }
    public object? Result { get; }
    public Microsoft.CodeAnalysis.Scripting.CompilationErrorException? CompilationException { get; }
    public Exception? RuntimeException { get; }

    private ScriptExecutionOutcome(
        ScriptExecutionOutcomeKind kind,
        object? result,
        Microsoft.CodeAnalysis.Scripting.CompilationErrorException? cex,
        Exception? rex)
    {
        Kind = kind;
        Result = result;
        CompilationException = cex;
        RuntimeException = rex;
    }

    public static ScriptExecutionOutcome Success(object? result) => new(ScriptExecutionOutcomeKind.Success, result, null, null);
    public static ScriptExecutionOutcome CompilationFailure(Microsoft.CodeAnalysis.Scripting.CompilationErrorException cex) => new(ScriptExecutionOutcomeKind.CompilationFailure, null, cex, null);
    public static ScriptExecutionOutcome Runtime(Exception ex) => new(ScriptExecutionOutcomeKind.Runtime, null, null, ex);
    public static ScriptExecutionOutcome TimedOut() => new(ScriptExecutionOutcomeKind.TimedOut, null, null, null);
    public static ScriptExecutionOutcome HardDeadline() => new(ScriptExecutionOutcomeKind.HardDeadline, null, null, null);
    public static ScriptExecutionOutcome OuterCancelled() => new(ScriptExecutionOutcomeKind.OuterCancelled, null, null, null);
    public static ScriptExecutionOutcome CapacityFailure() => new(ScriptExecutionOutcomeKind.CapacityFailure, null, null, null);
}
