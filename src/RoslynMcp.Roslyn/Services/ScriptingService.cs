using System.Diagnostics;
using System.Threading;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Roslyn C# scripting host with hard wall-clock cancellation guarantees.
///
/// FLAG-5C: <c>Microsoft.CodeAnalysis.CSharp.Scripting</c> does not honor
/// <see cref="CancellationToken"/> for tight CPU loops (e.g. <c>while(true){}</c>).
/// Older implementations awaited <c>CSharpScript.EvaluateAsync</c> directly and
/// would never return for such scripts.
///
/// This implementation gives FLAG-5C a defense-in-depth treatment:
///
/// 1. **Dedicated isolation thread per evaluation.** Each evaluation runs on its
///    own background <see cref="Thread"/>, never on the thread pool. A runaway
///    script CANNOT consume thread-pool slots and starve the rest of the host.
///    Thread is named for diagnostics.
///
/// 2. **Wall-clock hard deadline via <see cref="TaskCompletionSource"/> +
///    <see cref="Timer"/>.** The completion source is signaled by either the
///    script worker thread or by a <c>Timer</c> callback that fires after
///    <c>budget + grace</c> seconds. Whichever fires first wins; the
///    <c>EvaluateAsync</c> public API returns immediately at that point.
///
/// 3. **In-flight cap for new requests
///    (<see cref="ScriptingServiceOptions.MaxConcurrentEvaluations"/>).** Limits
///    how many evaluations may be racing to deadline at once. When the deadline
///    fires the slot is released — the request is done from the caller's
///    perspective; the worker thread is now "abandoned" and tracked separately.
///
/// 4. **Abandoned-thread cap
///    (<see cref="ScriptingServiceOptions.MaxAbandonedEvaluations"/>).** Hard
///    upper bound on how many leaked worker threads we will tolerate. New
///    requests are refused once this is hit, with an actionable error directing
///    the operator to restart the host.
///
/// 5. **Self-cleaning Timer-based heartbeat.** Heartbeat tear-down is purely
///    synchronous (<c>timer.Dispose()</c>); no awaited tasks in the finally
///    block can block the deadline race or the response.
///
/// 6. **Abandoned threads die with the host.** They are background threads
///    (<c>IsBackground = true</c>), so process exit disposes them. They may
///    hold a CPU core until they finish or the process exits.
/// </summary>
public sealed class ScriptingService : IScriptingService
{
    private readonly ILogger<ScriptingService> _logger;
    private readonly ScriptingServiceOptions _options;
    private readonly SemaphoreSlim _concurrencyGate;
    private long _activeEvaluations;
    private long _abandonedEvaluations;

    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Text.RegularExpressions",
        "System.Threading.Tasks",
        "System.IO"
    ];

    public ScriptingService(ILogger<ScriptingService> logger, ScriptingServiceOptions options)
    {
        _logger = logger;
        _options = options;
        var maxConcurrent = Math.Max(1, options.MaxConcurrentEvaluations);
        _concurrencyGate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public async Task<ScriptEvaluationDto> EvaluateAsync(
        string code,
        string[]? imports,
        CancellationToken ct,
        Action<ScriptEvaluationProgress>? onProgress = null,
        int? timeoutSecondsOverride = null)
    {
        var settings = CreateExecutionSettings(imports, timeoutSecondsOverride);

        var capacityError = await TryAcquireCapacityAsync(settings.EffectiveTimeoutSeconds, ct).ConfigureAwait(false);
        if (capacityError is not null)
        {
            return capacityError;
        }

        Interlocked.Increment(ref _activeEvaluations);

        // The script execution token is the inner token Roslyn sees. We cancel it when the
        // budget elapses, but Roslyn may not honor it for tight loops — that is what the
        // hard deadline below is for.
        using var timeoutCts = CreateTimeoutTokenSource(ct, settings.Budget);
        var execution = CreateExecutionContext();

        try
        {
            return await RunEvaluationAsync(code, onProgress, ct, timeoutCts, settings, execution).ConfigureAwait(false);
        }
        finally
        {
            DisposeTimers(execution);
            ReleaseConcurrencySlot(execution.RunState);
        }
    }

    private ScriptExecutionSettings CreateExecutionSettings(string[]? imports, int? timeoutSecondsOverride)
    {
        // UX-002: per-call timeout override; null/<=0 falls back to env-configured default.
        var effectiveTimeoutSeconds = timeoutSecondsOverride is > 0
            ? timeoutSecondsOverride.Value
            : _options.TimeoutSeconds;
        var graceSeconds = Math.Max(0, _options.WatchdogGraceSeconds);

        return new ScriptExecutionSettings(
            EffectiveTimeoutSeconds: effectiveTimeoutSeconds,
            GraceSeconds: graceSeconds,
            HardDeadlineSeconds: effectiveTimeoutSeconds + graceSeconds,
            HeartbeatInterval: TimeSpan.FromMilliseconds(Math.Max(100, _options.HeartbeatIntervalMs)),
            Budget: TimeSpan.FromSeconds(effectiveTimeoutSeconds),
            ScriptOptions: BuildScriptOptions(imports));
    }

    private static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken ct, TimeSpan budget)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(budget);
        return timeoutCts;
    }

    private static ScriptExecutionContext CreateExecutionContext() =>
        new(Stopwatch.StartNew(), new ScriptRunState(), CreateCompletionSource());

    private async Task<ScriptEvaluationDto> RunEvaluationAsync(
        string code,
        Action<ScriptEvaluationProgress>? onProgress,
        CancellationToken ct,
        CancellationTokenSource timeoutCts,
        ScriptExecutionSettings settings,
        ScriptExecutionContext execution)
    {
        var workerThread = StartWorkerThread(
            code,
            settings.ScriptOptions,
            timeoutCts.Token,
            execution.Completion,
            execution.RunState);
        execution.DeadlineTimer = CreateDeadlineTimer(execution.Completion, settings.HardDeadlineSeconds);
        execution.HeartbeatTimer = CreateHeartbeatTimer(
            onProgress,
            execution.Stopwatch,
            settings.Budget,
            settings.EffectiveTimeoutSeconds,
            settings.HeartbeatInterval,
            execution.RunState);

        // Outer cancellation propagation: if the caller cancels (e.g. MCP request budget),
        // surface OperationCanceledException promptly. Worker thread is then abandoned.
        using var ctRegistration = RegisterOuterCancellation(ct, execution.Completion);

        var raceOutcome = await execution.Completion.Task.ConfigureAwait(false);
        execution.Stopwatch.Stop();
        return FinalizeEvaluation(
            raceOutcome,
            execution.Stopwatch,
            workerThread.Name,
            execution.RunState,
            settings.HardDeadlineSeconds,
            settings.EffectiveTimeoutSeconds,
            settings.GraceSeconds,
            ct,
            timeoutCts);
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
        // Tear down timers (synchronous; never blocks).
        try { execution.HeartbeatTimer?.Dispose(); } catch { /* best-effort */ }
        try { execution.DeadlineTimer?.Dispose(); } catch { /* best-effort */ }
    }

    private static TaskCompletionSource<ScriptOutcome> CreateCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static CancellationTokenRegistration RegisterOuterCancellation(
        CancellationToken ct,
        TaskCompletionSource<ScriptOutcome> completion)
    {
        return ct.Register(static state =>
        {
            var tcs = (TaskCompletionSource<ScriptOutcome>)state!;
            tcs.TrySetResult(ScriptOutcome.OuterCancelled());
        }, completion);
    }

    private Thread StartWorkerThread(
        string code,
        ScriptOptions scriptOptions,
        CancellationToken timeoutToken,
        TaskCompletionSource<ScriptOutcome> completion,
        ScriptRunState runState)
    {
        // FLAG-5C: dedicated background thread, NOT a thread-pool thread.
        var workerThreadName = $"roslyn-mcp.script.{Guid.NewGuid():N}";
        if (workerThreadName.Length > 32)
        {
            workerThreadName = workerThreadName[..32];
        }

        var workerThread = new Thread(() =>
        {
            var outcome = ExecuteScript(code, scriptOptions, timeoutToken);
            CompleteWorkerThread(completion, outcome, runState);
        })
        {
            IsBackground = true,
            Name = workerThreadName,
        };
        workerThread.Start();
        return workerThread;
    }

    private static ScriptOutcome ExecuteScript(
        string code,
        ScriptOptions scriptOptions,
        CancellationToken timeoutToken)
    {
        try
        {
            var result = CSharpScript
                .EvaluateAsync<object?>(code, scriptOptions, cancellationToken: timeoutToken)
                .GetAwaiter().GetResult();
            return ScriptOutcome.Success(result);
        }
        catch (CompilationErrorException cex)
        {
            return ScriptOutcome.CompilationFailure(cex);
        }
        catch (OperationCanceledException)
        {
            return ScriptOutcome.TimedOut();
        }
        catch (Exception ex)
        {
            return ScriptOutcome.Runtime(ex);
        }
    }

    private void CompleteWorkerThread(
        TaskCompletionSource<ScriptOutcome> completion,
        ScriptOutcome outcome,
        ScriptRunState runState)
    {
        Interlocked.Exchange(ref runState.WorkerFinished, 1);

        // If the worker was already declared abandoned by the deadline path, recover the
        // abandoned counter. Otherwise signal completion normally.
        if (Interlocked.Read(ref runState.AbandonedFlag) == 1)
        {
            Interlocked.Decrement(ref _abandonedEvaluations);
            return;
        }

        completion.TrySetResult(outcome);
    }

    private static Timer CreateDeadlineTimer(
        TaskCompletionSource<ScriptOutcome> completion,
        int hardDeadlineSeconds)
    {
        // Wall-clock hard deadline. Timer fires on a thread-pool thread, but TrySetResult
        // is non-blocking and the awaiting frame uses RunContinuationsAsynchronously so the
        // continuation runs on a fresh thread-pool thread, never inline on the timer thread.
        return new Timer(
            _ => completion.TrySetResult(ScriptOutcome.HardDeadline()),
            state: null,
            dueTime: TimeSpan.FromSeconds(hardDeadlineSeconds),
            period: Timeout.InfiniteTimeSpan);
    }

    private Timer CreateHeartbeatTimer(
        Action<ScriptEvaluationProgress>? onProgress,
        Stopwatch sw,
        TimeSpan budget,
        int effectiveTimeoutSeconds,
        TimeSpan heartbeatInterval,
        ScriptRunState runState)
    {
        // Heartbeat is also Timer-based so its tear-down is sync (timer.Dispose).
        return new Timer(
            _ => EmitHeartbeat(onProgress, sw, budget, effectiveTimeoutSeconds, runState),
            state: null,
            dueTime: heartbeatInterval,
            period: heartbeatInterval);
    }

    private void EmitHeartbeat(
        Action<ScriptEvaluationProgress>? onProgress,
        Stopwatch sw,
        TimeSpan budget,
        int effectiveTimeoutSeconds,
        ScriptRunState runState)
    {
        var index = Interlocked.Increment(ref runState.HeartbeatCount);
        var elapsed = sw.Elapsed;
        onProgress?.Invoke(new ScriptEvaluationProgress(elapsed, budget, index));
        if (index == 1)
        {
            _logger.LogInformation(
                "evaluate_csharp: Roslyn script evaluation in progress (budget {BudgetSeconds}s, heartbeat every {HeartbeatMs}ms)",
                effectiveTimeoutSeconds,
                _options.HeartbeatIntervalMs);
        }

        if (runState.SlowWarningEmitted || elapsed.TotalSeconds < _options.StuckWarningSeconds)
        {
            return;
        }

        runState.SlowWarningEmitted = true;
        _logger.LogWarning(
            "evaluate_csharp: still running after {ElapsedSeconds:F1}s — clients often show a static \"Evaluating C#\" step here; " +
            "large compile or synchronous script work may continue until timeout ({BudgetSeconds}s).",
            elapsed.TotalSeconds,
            effectiveTimeoutSeconds);
    }

    private ScriptEvaluationDto FinalizeEvaluation(
        ScriptOutcome raceOutcome,
        Stopwatch sw,
        string? workerThreadName,
        ScriptRunState runState,
        int hardDeadlineSeconds,
        int effectiveTimeoutSeconds,
        int graceSeconds,
        CancellationToken ct,
        CancellationTokenSource timeoutCts)
    {
        // Outer cancellation: surface as OperationCanceledException; mark worker abandoned.
        if (raceOutcome.Kind == ScriptOutcomeKind.OuterCancelled)
        {
            MarkAbandonedIfWorkerStillRunning(timeoutCts, runState);
            ct.ThrowIfCancellationRequested();
        }

        // Hard deadline path — Roslyn never finished. Mark thread abandoned and return.
        if (raceOutcome.Kind == ScriptOutcomeKind.HardDeadline)
        {
            if (MarkAbandonedIfWorkerStillRunning(timeoutCts, runState))
            {
                LogHardDeadlineCritical(workerThreadName, hardDeadlineSeconds, effectiveTimeoutSeconds, graceSeconds);
            }

            return BuildHardDeadlineDto(
                sw,
                runState.HeartbeatCount,
                hardDeadlineSeconds,
                effectiveTimeoutSeconds,
                graceSeconds);
        }

        // Worker finished first — translate the outcome.
        return BuildOutcomeDto(raceOutcome, sw, runState.HeartbeatCount, effectiveTimeoutSeconds);
    }

    private async Task<ScriptEvaluationDto?> TryAcquireCapacityAsync(int effectiveTimeoutSeconds, CancellationToken ct)
    {
        var abandonedAtEntry = Interlocked.Read(ref _abandonedEvaluations);
        if (abandonedAtEntry >= _options.MaxAbandonedEvaluations)
        {
            return CapacityFailure(
                $"evaluate_csharp has {abandonedAtEntry} abandoned worker threads (cap {_options.MaxAbandonedEvaluations}). " +
                "Likely caused by previous infinite-loop scripts that Roslyn could not cancel. " +
                "Restart the MCP host to recover.",
                effectiveTimeoutSeconds);
        }

        var slotAcquireTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.ConcurrencySlotAcquireTimeoutSeconds));
        if (!await _concurrencyGate.WaitAsync(slotAcquireTimeout, ct).ConfigureAwait(false))
        {
            var activeAtCap = Interlocked.Read(ref _activeEvaluations);
            return CapacityFailure(
                $"evaluate_csharp is at capacity ({_options.MaxConcurrentEvaluations} concurrent slots, {activeAtCap} in flight). " +
                "Wait briefly or raise ROSLYNMCP_SCRIPT_MAX_CONCURRENT.",
                effectiveTimeoutSeconds);
        }

        return null; // capacity acquired successfully
    }

    private ScriptEvaluationDto CapacityFailure(string error, int effectiveTimeoutSeconds)
    {
        return new ScriptEvaluationDto(
            Success: false,
            ResultType: null,
            ResultValue: null,
            Error: error,
            CompilationErrors: null,
            ElapsedMs: 0,
            AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
            ProgressHeartbeatCount: null);
    }

    /// <summary>
    /// Atomically marks the worker thread as abandoned if it has not yet finished, and signals
    /// inner-token cancellation. Returns <c>true</c> if this call is the one that flipped the
    /// flag (so callers can perform once-only side-effects like critical logging).
    /// </summary>
    private bool MarkAbandonedIfWorkerStillRunning(
        CancellationTokenSource timeoutCts,
        ScriptRunState runState)
    {
        if (Interlocked.Read(ref runState.WorkerFinished) != 0) return false;
        if (Interlocked.Exchange(ref runState.AbandonedFlag, 1) != 0) return false;

        Interlocked.Increment(ref _abandonedEvaluations);
        try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    private void LogHardDeadlineCritical(
        string? workerThreadName,
        int hardDeadlineSeconds,
        int effectiveTimeoutSeconds,
        int graceSeconds)
    {
        _logger.LogCritical(
            "evaluate_csharp WATCHDOG: hard deadline ({HardDeadlineSeconds}s = budget {BudgetSeconds}s + grace {GraceSeconds}s) " +
            "elapsed; abandoning script worker thread '{ThreadName}'. The thread continues until it returns or the host exits. " +
            "Active evaluations: {Active}, abandoned: {Abandoned} (cap {AbandonedCap}).",
            hardDeadlineSeconds,
            effectiveTimeoutSeconds,
            graceSeconds,
            workerThreadName,
            Interlocked.Read(ref _activeEvaluations),
            Interlocked.Read(ref _abandonedEvaluations),
            _options.MaxAbandonedEvaluations);
    }

    private ScriptEvaluationDto BuildHardDeadlineDto(
        Stopwatch sw,
        int heartbeatCount,
        int hardDeadlineSeconds,
        int effectiveTimeoutSeconds,
        int graceSeconds)
    {
        return new ScriptEvaluationDto(
            Success: false,
            ResultType: null,
            ResultValue: null,
            Error: $"Script execution was forcibly abandoned after {hardDeadlineSeconds} second(s) " +
                   $"(script budget {effectiveTimeoutSeconds}s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS {graceSeconds}s). " +
                   "Roslyn does not cancel tight infinite loops; the server no longer waits for cooperative cancellation. " +
                   $"{Interlocked.Read(ref _abandonedEvaluations)}/{_options.MaxAbandonedEvaluations} abandoned worker thread(s) outstanding; " +
                   "restart the MCP host if this happens repeatedly.",
            CompilationErrors: null,
            ElapsedMs: sw.ElapsedMilliseconds,
            AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
            ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null);
    }

    /// <summary>
    /// Translates a worker-thread <see cref="ScriptOutcome"/> into a <see cref="ScriptEvaluationDto"/>.
    /// All branches share elapsed-time, applied-timeout, and heartbeat fields; only the success
    /// flag, payload, and error message change per outcome kind.
    /// </summary>
    private static ScriptEvaluationDto BuildOutcomeDto(
        ScriptOutcome outcome,
        Stopwatch sw,
        int heartbeatCount,
        int effectiveTimeoutSeconds)
    {
        var (success, resultType, resultValue, error, compilationErrors) = outcome.Kind switch
        {
            ScriptOutcomeKind.Success => (
                true,
                outcome.Result?.GetType().FullName,
                FormatResult(outcome.Result),
                (string?)null,
                (List<DiagnosticDto>?)null),
            ScriptOutcomeKind.CompilationFailure => (
                false,
                (string?)null,
                (string?)null,
                outcome.CompilationException!.Message,
                MapCompilationErrors(outcome.CompilationException!)),
            ScriptOutcomeKind.TimedOut => (
                false,
                (string?)null,
                (string?)null,
                $"Script execution timed out after {effectiveTimeoutSeconds} seconds. " +
                "Pass timeoutSeconds on the call (UX-002) or set ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS to raise the default.",
                (List<DiagnosticDto>?)null),
            ScriptOutcomeKind.Runtime => (
                false,
                (string?)null,
                (string?)null,
                $"Runtime error: {outcome.RuntimeException!.GetType().Name}: {outcome.RuntimeException!.Message}",
                (List<DiagnosticDto>?)null),
            _ => (
                false,
                (string?)null,
                (string?)null,
                "Unknown script evaluation outcome (internal error).",
                (List<DiagnosticDto>?)null),
        };

        return new ScriptEvaluationDto(
            Success: success,
            ResultType: resultType,
            ResultValue: resultValue,
            Error: error,
            CompilationErrors: compilationErrors,
            ElapsedMs: sw.ElapsedMilliseconds,
            AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
            ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null);
    }

    private ScriptOptions BuildScriptOptions(string[]? imports)
    {
        var allImports = DefaultImports.Concat(imports ?? []).Distinct().ToArray();
        return ScriptOptions.Default
            .WithImports(allImports)
            .WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(System.Text.RegularExpressions.Regex).Assembly)
            .WithEmitDebugInformation(false);
    }

    private static List<DiagnosticDto> MapCompilationErrors(CompilationErrorException ex)
    {
        return ex.Diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .Select(d =>
            {
                var lineSpan = d.Location.GetMappedLineSpan();
                return new DiagnosticDto(
                    Id: d.Id,
                    Message: d.GetMessage(),
                    Severity: d.Severity.ToString(),
                    Category: d.Descriptor.Category,
                    FilePath: null,
                    StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
                    StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
                    EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
                    EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null);
            })
            .ToList();
    }

    private static string? FormatResult(object? result)
    {
        if (result is null) return "null";

        var str = result.ToString();
        if (str is not null && str.Length > 4096)
            str = str[..4093] + "...";

        return str;
    }

    private sealed class ScriptRunState
    {
        public int HeartbeatCount;
        public bool SlowWarningEmitted;
        public long WorkerFinished;
        public long SlotReleased;
        public long AbandonedFlag;
    }

    private sealed class ScriptExecutionContext(
        Stopwatch stopwatch,
        ScriptRunState runState,
        TaskCompletionSource<ScriptOutcome> completion)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public ScriptRunState RunState { get; } = runState;
        public TaskCompletionSource<ScriptOutcome> Completion { get; } = completion;
        public Timer? HeartbeatTimer { get; set; }
        public Timer? DeadlineTimer { get; set; }
    }

    private readonly record struct ScriptExecutionSettings(
        int EffectiveTimeoutSeconds,
        int GraceSeconds,
        int HardDeadlineSeconds,
        TimeSpan HeartbeatInterval,
        TimeSpan Budget,
        ScriptOptions ScriptOptions);

    private enum ScriptOutcomeKind
    {
        Success,
        CompilationFailure,
        Runtime,
        TimedOut,
        HardDeadline,
        OuterCancelled,
    }

    private readonly struct ScriptOutcome
    {
        public ScriptOutcomeKind Kind { get; }
        public object? Result { get; }
        public CompilationErrorException? CompilationException { get; }
        public Exception? RuntimeException { get; }

        private ScriptOutcome(ScriptOutcomeKind kind, object? result, CompilationErrorException? cex, Exception? rex)
        {
            Kind = kind;
            Result = result;
            CompilationException = cex;
            RuntimeException = rex;
        }

        public static ScriptOutcome Success(object? result) => new(ScriptOutcomeKind.Success, result, null, null);
        public static ScriptOutcome CompilationFailure(CompilationErrorException cex) => new(ScriptOutcomeKind.CompilationFailure, null, cex, null);
        public static ScriptOutcome Runtime(Exception ex) => new(ScriptOutcomeKind.Runtime, null, null, ex);
        public static ScriptOutcome TimedOut() => new(ScriptOutcomeKind.TimedOut, null, null, null);
        public static ScriptOutcome HardDeadline() => new(ScriptOutcomeKind.HardDeadline, null, null, null);
        public static ScriptOutcome OuterCancelled() => new(ScriptOutcomeKind.OuterCancelled, null, null, null);
    }
}
