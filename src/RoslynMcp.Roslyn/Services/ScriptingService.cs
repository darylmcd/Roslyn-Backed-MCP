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

    /// <summary>Number of evaluations currently racing to deadline (visible for tests/diagnostics).</summary>
    internal long ActiveEvaluations => Interlocked.Read(ref _activeEvaluations);

    /// <summary>Number of worker threads still running after their request returned via the deadline path.</summary>
    internal long AbandonedEvaluations => Interlocked.Read(ref _abandonedEvaluations);

    public async Task<ScriptEvaluationDto> EvaluateAsync(
        string code,
        string[]? imports,
        CancellationToken ct,
        Action<ScriptEvaluationProgress>? onProgress = null,
        int? timeoutSecondsOverride = null)
    {
        // UX-002: per-call timeout override; null/<=0 falls back to env-configured default.
        var effectiveTimeoutSeconds = timeoutSecondsOverride is > 0
            ? timeoutSecondsOverride.Value
            : _options.TimeoutSeconds;
        var graceSeconds = Math.Max(0, _options.WatchdogGraceSeconds);
        var hardDeadlineSeconds = effectiveTimeoutSeconds + graceSeconds;
        var heartbeatInterval = TimeSpan.FromMilliseconds(Math.Max(100, _options.HeartbeatIntervalMs));

        // FLAG-5C: refuse if too many leaked worker threads have already accumulated.
        var abandonedAtEntry = Interlocked.Read(ref _abandonedEvaluations);
        if (abandonedAtEntry >= _options.MaxAbandonedEvaluations)
        {
            return CapacityFailure(
                $"evaluate_csharp has {abandonedAtEntry} abandoned worker threads (cap {_options.MaxAbandonedEvaluations}). " +
                "Likely caused by previous infinite-loop scripts that Roslyn could not cancel. " +
                "Restart the MCP host to recover.",
                effectiveTimeoutSeconds);
        }

        // FLAG-5C: cap concurrent in-flight evaluations so the dedicated-thread-per-evaluation
        // model has a hard ceiling on simultaneous compilations as well. Slot is released as
        // soon as the request completes (success, error, or hard deadline), so a runaway worker
        // does NOT keep the slot occupied — it transitions into the abandoned-thread bookkeeping.
        var slotAcquireTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.ConcurrencySlotAcquireTimeoutSeconds));
        if (!await _concurrencyGate.WaitAsync(slotAcquireTimeout, ct).ConfigureAwait(false))
        {
            var activeAtCap = Interlocked.Read(ref _activeEvaluations);
            return CapacityFailure(
                $"evaluate_csharp is at capacity ({_options.MaxConcurrentEvaluations} concurrent slots, {activeAtCap} in flight). " +
                "Wait briefly or raise ROSLYNMCP_SCRIPT_MAX_CONCURRENT.",
                effectiveTimeoutSeconds);
        }

        Interlocked.Increment(ref _activeEvaluations);

        var sw = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(effectiveTimeoutSeconds);
        var scriptOptions = BuildScriptOptions(imports);

        // The script execution token is the inner token Roslyn sees. We cancel it when the
        // budget elapses, but Roslyn may not honor it for tight loops — that is what the
        // hard deadline below is for.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(budget);

        var heartbeatCount = 0;
        var slowWarningEmitted = false;
        Timer? heartbeatTimer = null;
        Timer? deadlineTimer = null;

        // Signaled by EXACTLY ONE of: the worker thread (success/error/cancel) or the deadline timer.
        // RunContinuationsAsynchronously ensures the awaiting frame is not invoked inline on the
        // worker thread or the timer thread.
        var completion = new TaskCompletionSource<ScriptOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        long workerFinished = 0;
        long slotReleased = 0;
        long abandonedFlag = 0;

        void ReleaseSlotOnce()
        {
            if (Interlocked.Exchange(ref slotReleased, 1) == 0)
            {
                Interlocked.Decrement(ref _activeEvaluations);
                try { _concurrencyGate.Release(); }
                catch (ObjectDisposedException) { /* host shutting down */ }
                catch (SemaphoreFullException) { /* defensive */ }
            }
        }

        try
        {
            // FLAG-5C: dedicated background thread, NOT a thread-pool thread.
            var workerThreadName = $"roslyn-mcp.script.{Guid.NewGuid():N}";
            if (workerThreadName.Length > 32) workerThreadName = workerThreadName[..32];
            var workerThread = new Thread(() =>
            {
                ScriptOutcome outcome;
                try
                {
                    var result = CSharpScript
                        .EvaluateAsync<object?>(code, scriptOptions, cancellationToken: timeoutCts.Token)
                        .GetAwaiter().GetResult();
                    outcome = ScriptOutcome.Success(result);
                }
                catch (CompilationErrorException cex)
                {
                    outcome = ScriptOutcome.CompilationFailure(cex);
                }
                catch (OperationCanceledException)
                {
                    outcome = ScriptOutcome.TimedOut();
                }
                catch (Exception ex)
                {
                    outcome = ScriptOutcome.Runtime(ex);
                }

                Interlocked.Exchange(ref workerFinished, 1);

                // If the worker was already declared abandoned by the deadline path, recover the
                // abandoned counter. Otherwise signal completion normally.
                if (Interlocked.Read(ref abandonedFlag) == 1)
                {
                    Interlocked.Decrement(ref _abandonedEvaluations);
                }
                else
                {
                    completion.TrySetResult(outcome);
                }
            })
            {
                IsBackground = true,
                Name = workerThreadName,
            };
            workerThread.Start();

            // Wall-clock hard deadline. Timer fires on a thread-pool thread, but TrySetResult
            // is non-blocking and the awaiting frame uses RunContinuationsAsynchronously so the
            // continuation runs on a fresh thread-pool thread, never inline on the timer thread.
            deadlineTimer = new Timer(
                _ => completion.TrySetResult(ScriptOutcome.HardDeadline()),
                state: null,
                dueTime: TimeSpan.FromSeconds(hardDeadlineSeconds),
                period: Timeout.InfiniteTimeSpan);

            // Heartbeat is also Timer-based so its tear-down is sync (timer.Dispose).
            heartbeatTimer = new Timer(
                _ =>
                {
                    var index = Interlocked.Increment(ref heartbeatCount);
                    var elapsed = sw.Elapsed;
                    onProgress?.Invoke(new ScriptEvaluationProgress(elapsed, budget, index));
                    if (index == 1)
                    {
                        _logger.LogInformation(
                            "evaluate_csharp: Roslyn script evaluation in progress (budget {BudgetSeconds}s, heartbeat every {HeartbeatMs}ms)",
                            effectiveTimeoutSeconds,
                            _options.HeartbeatIntervalMs);
                    }
                    if (!slowWarningEmitted && elapsed.TotalSeconds >= _options.StuckWarningSeconds)
                    {
                        slowWarningEmitted = true;
                        _logger.LogWarning(
                            "evaluate_csharp: still running after {ElapsedSeconds:F1}s — clients often show a static \"Evaluating C#\" step here; " +
                            "large compile or synchronous script work may continue until timeout ({BudgetSeconds}s).",
                            elapsed.TotalSeconds,
                            effectiveTimeoutSeconds);
                    }
                },
                state: null,
                dueTime: heartbeatInterval,
                period: heartbeatInterval);

            // Outer cancellation propagation: if the caller cancels (e.g. MCP request budget),
            // surface OperationCanceledException promptly. Worker thread is then abandoned.
            using var ctRegistration = ct.Register(static state =>
            {
                var tcs = (TaskCompletionSource<ScriptOutcome>)state!;
                tcs.TrySetResult(ScriptOutcome.OuterCancelled());
            }, completion);

            var raceOutcome = await completion.Task.ConfigureAwait(false);
            sw.Stop();

            // Outer cancellation: surface as OperationCanceledException; mark worker abandoned.
            if (raceOutcome.Kind == ScriptOutcomeKind.OuterCancelled)
            {
                if (Interlocked.Read(ref workerFinished) == 0 &&
                    Interlocked.Exchange(ref abandonedFlag, 1) == 0)
                {
                    Interlocked.Increment(ref _abandonedEvaluations);
                    try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                }
                ct.ThrowIfCancellationRequested();
            }

            // Hard deadline path — Roslyn never finished. Mark thread abandoned and return.
            if (raceOutcome.Kind == ScriptOutcomeKind.HardDeadline)
            {
                if (Interlocked.Read(ref workerFinished) == 0 &&
                    Interlocked.Exchange(ref abandonedFlag, 1) == 0)
                {
                    Interlocked.Increment(ref _abandonedEvaluations);
                    try { timeoutCts.Cancel(); } catch (ObjectDisposedException) { }
                    _logger.LogCritical(
                        "evaluate_csharp WATCHDOG: hard deadline ({HardDeadlineSeconds}s = budget {BudgetSeconds}s + grace {GraceSeconds}s) " +
                        "elapsed; abandoning script worker thread '{ThreadName}'. The thread continues until it returns or the host exits. " +
                        "Active evaluations: {Active}, abandoned: {Abandoned} (cap {AbandonedCap}).",
                        hardDeadlineSeconds,
                        effectiveTimeoutSeconds,
                        graceSeconds,
                        workerThread.Name,
                        Interlocked.Read(ref _activeEvaluations),
                        Interlocked.Read(ref _abandonedEvaluations),
                        _options.MaxAbandonedEvaluations);
                }

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

            // Worker finished first — translate the outcome.
            return raceOutcome.Kind switch
            {
                ScriptOutcomeKind.Success => new ScriptEvaluationDto(
                    Success: true,
                    ResultType: raceOutcome.Result?.GetType().FullName,
                    ResultValue: FormatResult(raceOutcome.Result),
                    Error: null,
                    CompilationErrors: null,
                    ElapsedMs: sw.ElapsedMilliseconds,
                    AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                    ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null),
                ScriptOutcomeKind.CompilationFailure => new ScriptEvaluationDto(
                    Success: false,
                    ResultType: null,
                    ResultValue: null,
                    Error: raceOutcome.CompilationException!.Message,
                    CompilationErrors: MapCompilationErrors(raceOutcome.CompilationException!),
                    ElapsedMs: sw.ElapsedMilliseconds,
                    AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                    ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null),
                ScriptOutcomeKind.TimedOut => new ScriptEvaluationDto(
                    Success: false,
                    ResultType: null,
                    ResultValue: null,
                    Error: $"Script execution timed out after {effectiveTimeoutSeconds} seconds. " +
                           "Pass timeoutSeconds on the call (UX-002) or set ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS to raise the default.",
                    CompilationErrors: null,
                    ElapsedMs: sw.ElapsedMilliseconds,
                    AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                    ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null),
                ScriptOutcomeKind.Runtime => new ScriptEvaluationDto(
                    Success: false,
                    ResultType: null,
                    ResultValue: null,
                    Error: $"Runtime error: {raceOutcome.RuntimeException!.GetType().Name}: {raceOutcome.RuntimeException!.Message}",
                    CompilationErrors: null,
                    ElapsedMs: sw.ElapsedMilliseconds,
                    AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                    ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null),
                _ => new ScriptEvaluationDto(
                    Success: false,
                    ResultType: null,
                    ResultValue: null,
                    Error: "Unknown script evaluation outcome (internal error).",
                    CompilationErrors: null,
                    ElapsedMs: sw.ElapsedMilliseconds,
                    AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                    ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null),
            };
        }
        finally
        {
            // Tear down timers (synchronous; never blocks).
            try { heartbeatTimer?.Dispose(); } catch { /* best-effort */ }
            try { deadlineTimer?.Dispose(); } catch { /* best-effort */ }

            // Always release the slot. The request is done from the caller's perspective. If
            // the worker thread is still running it has been moved to the abandoned counter and
            // will not consume a slot.
            ReleaseSlotOnce();
        }
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
