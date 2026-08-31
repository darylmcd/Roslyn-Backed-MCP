using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Services;

internal sealed class ScriptExecutionSupervisor
{
    private const int CancellationPollMilliseconds = 50;
    private readonly ILogger _logger;
    private readonly ScriptingServiceOptions _options;
    private readonly IScriptWorkerProcess _workerProcess;
    private readonly SemaphoreSlim _concurrencyGate;
    private long _activeEvaluations;
    private long _abandonedEvaluations;

    public ScriptExecutionSupervisor(ILogger logger, ScriptingServiceOptions options)
        : this(logger, options, new ScriptWorkerProcess(logger)) { }

    internal ScriptExecutionSupervisor(ILogger logger, ScriptingServiceOptions options, IScriptWorkerProcess workerProcess)
    {
        _logger = logger;
        _options = options;
        _workerProcess = workerProcess;
        var maxConcurrent = Math.Max(1, options.MaxConcurrentEvaluations);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    internal long ActiveEvaluationCount => Interlocked.Read(ref _activeEvaluations);
    internal long AbandonedEvaluationCount => Interlocked.Read(ref _abandonedEvaluations);

    public Task<ScriptExecutionResult> ExecuteAsync(
        ScriptWorkerRequest request,
        Action<ScriptEvaluationProgress>? onProgress,
        ScriptExecutionSupervisorSettings settings,
        CancellationToken ct) => ExecuteCoreAsync(_workerProcess, request, onProgress, settings, ct);

    // Deterministic unit-test seam. Production always uses the process-backed overload.
    internal Task<ScriptExecutionResult> ExecuteAsync(
        Func<CancellationToken, ScriptExecutionOutcome> executeWorker,
        Action<ScriptEvaluationProgress>? onProgress,
        ScriptExecutionSupervisorSettings settings,
        CancellationToken ct) => ExecuteCoreAsync(
            new DelegateScriptWorkerProcess(executeWorker, settings.Budget),
            new ScriptWorkerRequest(string.Empty, null, settings.EffectiveTimeoutSeconds),
            onProgress,
            settings,
            ct);

    private async Task<ScriptExecutionResult> ExecuteCoreAsync(
        IScriptWorkerProcess workerProcess,
        ScriptWorkerRequest request,
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

        IScriptWorkerSession session;
        try
        {
            session = await workerProcess.StartAsync(request, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _activeEvaluations);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _concurrencyGate.Release();
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            _concurrencyGate.Release();
            throw;
        }

        var completion = new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new Thread(() => MonitorWorker(session, onProgress, settings, ct, completion))
        {
            IsBackground = true,
            Name = CreateMonitorName(),
        };
        try
        {
            monitor.Start();
        }
        catch
        {
            try { session.Terminate(); }
            finally
            {
                session.Dispose();
                Interlocked.Decrement(ref _activeEvaluations);
                _concurrencyGate.Release();
            }
            throw;
        }
        return await completion.Task.ConfigureAwait(false);
    }

    private void MonitorWorker(
        IScriptWorkerSession session,
        Action<ScriptEvaluationProgress>? onProgress,
        ScriptExecutionSupervisorSettings settings,
        CancellationToken ct,
        TaskCompletionSource<ScriptExecutionResult> completion)
    {
        var stopwatch = Stopwatch.StartNew();
        var workerName = session.Name;
        var observation = default(ScriptMonitorObservation);
        Exception? cleanupFailure = null;
        var workerAbandoned = false;

        try
        {
            observation = WaitForOutcome(session, onProgress, settings, ct, stopwatch);
            if (observation.Outcome.Kind is ScriptExecutionOutcomeKind.HardDeadline or ScriptExecutionOutcomeKind.OuterCancelled)
            {
                cleanupFailure = TryTerminate(session, out workerAbandoned);
            }
        }
        catch (Exception ex)
        {
            observation = new ScriptMonitorObservation(ScriptExecutionOutcome.Runtime(ex), observation.HeartbeatCount);
            cleanupFailure = TryTerminate(session, out workerAbandoned);
        }
        finally
        {
            stopwatch.Stop();
            cleanupFailure = Combine(cleanupFailure, DisposeAndRelease(session));
        }

        PublishResult(
            observation,
            cleanupFailure,
            workerAbandoned,
            workerName,
            stopwatch.ElapsedMilliseconds,
            settings,
            completion);
    }

    private void PublishResult(
        ScriptMonitorObservation observation,
        Exception? cleanupFailure,
        bool workerAbandoned,
        string workerName,
        long elapsedMilliseconds,
        ScriptExecutionSupervisorSettings settings,
        TaskCompletionSource<ScriptExecutionResult> completion)
    {
        if (cleanupFailure is not null)
        {
            _logger.LogError(
                cleanupFailure,
                "evaluate_csharp: isolated worker cleanup failed after outcome {OutcomeKind}; capacity integrity may require host restart.",
                observation.Outcome.Kind);
            if (observation.Outcome.Kind is ScriptExecutionOutcomeKind.None or ScriptExecutionOutcomeKind.Success)
            {
                observation = observation with
                {
                    Outcome = ScriptExecutionOutcome.Runtime(
                        new InvalidOperationException("The isolated script worker cleanup failed.")),
                };
            }
        }

        if (observation.Outcome.Kind == ScriptExecutionOutcomeKind.HardDeadline)
        {
            _logger.LogCritical(
                "evaluate_csharp WATCHDOG: hard deadline {HardDeadlineSeconds}s elapsed; isolated worker {WorkerName} " +
                "was {TerminationState}. Active evaluations: {Active}; unreclaimed workers: {Abandoned} (cap {AbandonedCap}).",
                settings.HardDeadlineSeconds,
                workerName,
                workerAbandoned ? "not reclaimed" : "terminated",
                Interlocked.Read(ref _activeEvaluations),
                Interlocked.Read(ref _abandonedEvaluations),
                _options.MaxAbandonedEvaluations);
        }

        completion.TrySetResult(ScriptExecutionResult.ForOutcome(
            observation.Outcome,
            elapsedMilliseconds,
            observation.HeartbeatCount,
            Interlocked.Read(ref _abandonedEvaluations),
            _options.MaxAbandonedEvaluations,
            settings));
    }

    private ScriptMonitorObservation WaitForOutcome(
        IScriptWorkerSession session,
        Action<ScriptEvaluationProgress>? onProgress,
        ScriptExecutionSupervisorSettings settings,
        CancellationToken ct,
        Stopwatch stopwatch)
    {
        var heartbeatCount = 0;
        var progressEnabled = true;
        var slowWarningEmitted = false;
        var nextHeartbeat = settings.HeartbeatInterval;
        var hardDeadline = TimeSpan.FromSeconds(settings.HardDeadlineSeconds);

        while (true)
        {
            if (ct.IsCancellationRequested)
                return new(ScriptExecutionOutcome.OuterCancelled(), heartbeatCount);
            if (stopwatch.Elapsed >= hardDeadline)
                return new(ScriptExecutionOutcome.HardDeadline(), heartbeatCount);

            var wait = MinPositive(
                nextHeartbeat - stopwatch.Elapsed,
                hardDeadline - stopwatch.Elapsed,
                TimeSpan.FromMilliseconds(CancellationPollMilliseconds));
            if (session.WaitForExit(Math.Max(1, (int)Math.Ceiling(wait.TotalMilliseconds))))
            {
                var outcome = ct.IsCancellationRequested
                    ? ScriptExecutionOutcome.OuterCancelled()
                    : session.GetOutcome();
                return new(outcome, heartbeatCount);
            }
            if (stopwatch.Elapsed < nextHeartbeat)
                continue;

            heartbeatCount++;
            progressEnabled = EmitHeartbeat(onProgress, progressEnabled, stopwatch.Elapsed, settings, heartbeatCount);
            if (!slowWarningEmitted && stopwatch.Elapsed.TotalSeconds >= _options.StuckWarningSeconds)
            {
                slowWarningEmitted = true;
                _logger.LogWarning(
                    "evaluate_csharp: isolated script still running after {ElapsedSeconds:F1}s; " +
                    "execution remains bounded by the {HardDeadlineSeconds}s hard deadline.",
                    stopwatch.Elapsed.TotalSeconds,
                    settings.HardDeadlineSeconds);
            }
            nextHeartbeat = stopwatch.Elapsed + settings.HeartbeatInterval;
        }
    }

    private Exception? TryTerminate(IScriptWorkerSession session, out bool workerAbandoned)
    {
        try
        {
            session.Terminate();
            workerAbandoned = false;
            return null;
        }
        catch (Exception ex)
        {
            workerAbandoned = true;
            Interlocked.Increment(ref _abandonedEvaluations);
            return ex;
        }
    }

    private Exception? DisposeAndRelease(IScriptWorkerSession session)
    {
        Exception? failure = null;
        try { session.Dispose(); }
        catch (Exception ex) { failure = ex; }

        Interlocked.Decrement(ref _activeEvaluations);
        try { _concurrencyGate.Release(); }
        catch (Exception ex) when (ex is ObjectDisposedException or SemaphoreFullException)
        {
            failure = Combine(failure, ex);
        }
        return failure;
    }

    private static Exception? Combine(Exception? first, Exception? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return new AggregateException(first, second);
    }

    private bool EmitHeartbeat(
        Action<ScriptEvaluationProgress>? onProgress,
        bool progressEnabled,
        TimeSpan elapsed,
        ScriptExecutionSupervisorSettings settings,
        int heartbeatCount)
    {
        if (heartbeatCount == 1)
        {
            _logger.LogInformation(
                "evaluate_csharp: isolated script evaluation in progress (budget {BudgetSeconds}s, heartbeat every {HeartbeatMs}ms)",
                settings.EffectiveTimeoutSeconds,
                settings.HeartbeatInterval.TotalMilliseconds);
        }
        if (!progressEnabled || onProgress is null)
        {
            return progressEnabled;
        }
        try
        {
            onProgress(new ScriptEvaluationProgress(elapsed, settings.Budget, heartbeatCount));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "evaluate_csharp: progress callback failed; disabling further callbacks for this evaluation.");
            return false;
        }
    }

    private async Task<ScriptExecutionCapacityFailure?> TryAcquireCapacityAsync(CancellationToken ct)
    {
        var abandonedAtEntry = Interlocked.Read(ref _abandonedEvaluations);
        if (abandonedAtEntry >= _options.MaxAbandonedEvaluations)
        {
            return new ScriptExecutionCapacityFailure(
                ScriptExecutionCapacityFailureKind.AbandonedWorkerCap, abandonedAtEntry, 0);
        }
        var slotAcquireTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.ConcurrencySlotAcquireTimeoutSeconds));
        if (!await _concurrencyGate.WaitAsync(slotAcquireTimeout, ct).ConfigureAwait(false))
        {
            return new ScriptExecutionCapacityFailure(
                ScriptExecutionCapacityFailureKind.ConcurrentSlotCap,
                abandonedAtEntry,
                Interlocked.Read(ref _activeEvaluations));
        }
        return null;
    }

    private static void ValidateSettings(ScriptExecutionSupervisorSettings settings)
    {
        var maximum = TimeSpan.FromSeconds(ScriptingServiceOptions.MaxTimerDurationSeconds);
        if (settings.EffectiveTimeoutSeconds < 1 || settings.Budget <= TimeSpan.Zero || settings.Budget > maximum)
            throw new ArgumentOutOfRangeException(nameof(settings), "Script budget is outside the supported range.");
        if (settings.HardDeadlineSeconds < 1 || settings.HardDeadlineSeconds > ScriptingServiceOptions.MaxTimerDurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(settings), "Hard deadline is outside the supported range.");
        if (settings.HeartbeatInterval <= TimeSpan.Zero || settings.HeartbeatInterval > maximum)
            throw new ArgumentOutOfRangeException(nameof(settings), "Heartbeat interval is outside the supported range.");
    }

    private static TimeSpan MinPositive(TimeSpan first, TimeSpan second, TimeSpan third) =>
        TimeSpan.FromTicks(Math.Max(1, Math.Min(first.Ticks, Math.Min(second.Ticks, third.Ticks))));

    private static string CreateMonitorName()
    {
        var name = $"roslyn-mcp.script-monitor.{Guid.NewGuid():N}";
        return name.Length > 48 ? name[..48] : name;
    }
}

internal sealed class DelegateScriptWorkerProcess(
    Func<CancellationToken, ScriptExecutionOutcome> executeWorker,
    TimeSpan budget) : IScriptWorkerProcess
{
    public Task<IScriptWorkerSession> StartAsync(
        ScriptWorkerRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IScriptWorkerSession>(new Session(executeWorker, budget));
    }

    private sealed class Session : IScriptWorkerSession
    {
        private readonly CancellationTokenSource _timeout = new();
        private readonly ManualResetEventSlim _completed = new();
        private readonly Thread _thread;
        private ScriptExecutionOutcome _outcome;
        private int _disposeRequested;
        private int _resourcesDisposed;

        public Session(Func<CancellationToken, ScriptExecutionOutcome> executeWorker, TimeSpan budget)
        {
            _timeout.CancelAfter(budget);
            _thread = new Thread(() =>
            {
                try { _outcome = executeWorker(_timeout.Token); }
                catch (OperationCanceledException) { _outcome = ScriptExecutionOutcome.TimedOut(); }
                catch (Exception ex) { _outcome = ScriptExecutionOutcome.Runtime(ex); }
                finally
                {
                    _completed.Set();
                    if (Volatile.Read(ref _disposeRequested) != 0)
                    {
                        DisposeResources();
                    }
                }
            })
            { IsBackground = true, Name = "roslyn-mcp.script-test-worker" };
            _thread.Start();
        }

        public string Name => _thread.Name ?? "delegate-worker";
        public bool WaitForExit(int milliseconds) => _completed.Wait(milliseconds);
        public ScriptExecutionOutcome GetOutcome() => _outcome;
        public void Terminate() => _timeout.Cancel();
        public void Dispose()
        {
            if (_completed.IsSet)
            {
                DisposeResources();
                return;
            }
            Volatile.Write(ref _disposeRequested, 1);
            if (_completed.IsSet)
            {
                DisposeResources();
            }
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            {
                return;
            }
            _timeout.Dispose();
            _completed.Dispose();
        }
    }
}

internal readonly record struct ScriptExecutionSupervisorSettings(
    int EffectiveTimeoutSeconds, int GraceSeconds, int HardDeadlineSeconds, TimeSpan HeartbeatInterval, TimeSpan Budget);

internal readonly record struct ScriptMonitorObservation(
    ScriptExecutionOutcome Outcome,
    int HeartbeatCount);

internal enum ScriptExecutionCapacityFailureKind { AbandonedWorkerCap, ConcurrentSlotCap }
internal readonly record struct ScriptExecutionCapacityFailure(ScriptExecutionCapacityFailureKind Kind, long AbandonedCount, long ActiveCount);

internal sealed record ScriptExecutionResult(
    ScriptExecutionCapacityFailure? CapacityFailure,
    ScriptExecutionOutcome Outcome,
    long ElapsedMs,
    int HeartbeatCount,
    long AbandonedCount,
    int MaxAbandonedCount,
    ScriptExecutionSupervisorSettings Settings)
{
    public static ScriptExecutionResult ForCapacityFailure(ScriptExecutionCapacityFailure failure, ScriptExecutionSupervisorSettings settings) =>
        new(failure, ScriptExecutionOutcome.CapacityFailure(), 0, 0, failure.AbandonedCount, 0, settings);
    public static ScriptExecutionResult ForOutcome(
        ScriptExecutionOutcome outcome, long elapsedMs, int heartbeatCount, long abandonedCount, int maxAbandonedCount,
        ScriptExecutionSupervisorSettings settings) =>
        new(null, outcome, elapsedMs, heartbeatCount, abandonedCount, maxAbandonedCount, settings);
}

internal enum ScriptExecutionOutcomeKind
{
    None, Success, CompilationFailure, Runtime, TimedOut, HardDeadline, OuterCancelled, CapacityFailure,
}

internal readonly record struct ScriptExecutionOutcome(
    ScriptExecutionOutcomeKind Kind,
    string? ResultType,
    string? ResultValue,
    string? Error,
    List<DiagnosticDto>? CompilationErrors)
{
    [JsonIgnore]
    public object? Result { get; init; }

    [JsonIgnore]
    public Exception? RuntimeException { get; init; }

    [JsonIgnore]
    public Microsoft.CodeAnalysis.Scripting.CompilationErrorException? CompilationException { get; init; }

    public static ScriptExecutionOutcome Success(object? result) => new(
        ScriptExecutionOutcomeKind.Success, result?.GetType().FullName, ScriptingService.FormatResult(result), null, null)
    { Result = result };
    public static ScriptExecutionOutcome CompilationFailure(Microsoft.CodeAnalysis.Scripting.CompilationErrorException ex) => new(
        ScriptExecutionOutcomeKind.CompilationFailure, null, null, ex.Message, ScriptingService.MapCompilationErrors(ex))
    { CompilationException = ex };
    public static ScriptExecutionOutcome Runtime(Exception ex) => new(
        ScriptExecutionOutcomeKind.Runtime, null, null, $"Runtime error: {ex.GetType().Name}: {ex.Message}", null)
    { RuntimeException = ex };
    public static ScriptExecutionOutcome TimedOut() => new(ScriptExecutionOutcomeKind.TimedOut, null, null, null, null);
    public static ScriptExecutionOutcome HardDeadline() => new(ScriptExecutionOutcomeKind.HardDeadline, null, null, null, null);
    public static ScriptExecutionOutcome OuterCancelled() => new(ScriptExecutionOutcomeKind.OuterCancelled, null, null, null, null);
    public static ScriptExecutionOutcome CapacityFailure() => new(ScriptExecutionOutcomeKind.CapacityFailure, null, null, null, null);
}
