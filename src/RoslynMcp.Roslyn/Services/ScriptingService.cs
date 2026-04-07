using System.Diagnostics;
using System.Threading;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class ScriptingService : IScriptingService
{
    private readonly ILogger<ScriptingService> _logger;
    private readonly ScriptingServiceOptions _options;

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
    }

    public async Task<ScriptEvaluationDto> EvaluateAsync(
        string code,
        string[]? imports,
        CancellationToken ct,
        Action<ScriptEvaluationProgress>? onProgress = null,
        int? timeoutSecondsOverride = null)
    {
        // UX-002: Honor a per-call timeout override when supplied; fall back to the env-configured
        // default. Negative or zero values are treated as "use the default" so the caller cannot
        // accidentally disable the cancellation budget.
        var effectiveTimeoutSeconds = timeoutSecondsOverride is > 0
            ? timeoutSecondsOverride.Value
            : _options.TimeoutSeconds;

        var sw = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(effectiveTimeoutSeconds);
        var heartbeatInterval = TimeSpan.FromMilliseconds(_options.HeartbeatIntervalMs);
        var scriptOptions = BuildScriptOptions(imports);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeoutSeconds));

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatCount = 0;
        var slowWarningEmitted = false;

        Task? heartbeatTask = null;
        if (onProgress is not null || _logger.IsEnabled(LogLevel.Information) || _logger.IsEnabled(LogLevel.Warning))
        {
            heartbeatTask = RunHeartbeatAsync(
                sw,
                budget,
                heartbeatInterval,
                heartbeatCts.Token,
                p =>
                {
                    heartbeatCount = p.HeartbeatIndex;
                    onProgress?.Invoke(p);
                    if (p.HeartbeatIndex == 1)
                    {
                        _logger.LogInformation(
                            "evaluate_csharp: Roslyn script evaluation in progress (budget {BudgetSeconds}s, heartbeat every {HeartbeatMs}ms)",
                            effectiveTimeoutSeconds,
                            _options.HeartbeatIntervalMs);
                    }

                    if (!slowWarningEmitted && p.Elapsed.TotalSeconds >= _options.StuckWarningSeconds)
                    {
                        slowWarningEmitted = true;
                        _logger.LogWarning(
                            "evaluate_csharp: still running after {ElapsedSeconds:F1}s — clients often show a static \"Evaluating C#\" step here; " +
                            "large compile or synchronous script work may continue until timeout ({BudgetSeconds}s). " +
                            "Tune ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS / ROSLYNMCP_SCRIPT_STUCK_WARNING_SECONDS if needed.",
                            p.Elapsed.TotalSeconds,
                            effectiveTimeoutSeconds);
                    }
                });
        }

        var watchdogCompleted = 0;
        var firstWatchdogDueMs = Math.Max(0, (effectiveTimeoutSeconds + _options.WatchdogGraceSeconds) * 1000);
        Timer? watchdogTimer = new Timer(
            _ =>
            {
                if (Volatile.Read(ref watchdogCompleted) != 0)
                    return;

                var elapsed = sw.Elapsed;
                _logger.LogCritical(
                    "evaluate_csharp WATCHDOG: still running after {ElapsedSeconds:F1}s (script budget {BudgetSeconds}s + grace {GraceSeconds}s). " +
                    "Roslyn may not be honoring cancellation during compilation or execution; restart the MCP host if this repeats. " +
                    "If the MCP stderr shows no evaluate_csharp lines for tens of minutes while the IDE shows \"Evaluating C#\", the stall is likely client-side (agent/orchestrator), not this tool completing slowly.",
                    elapsed.TotalSeconds,
                    effectiveTimeoutSeconds,
                    _options.WatchdogGraceSeconds);
            },
            state: null,
            dueTime: TimeSpan.FromMilliseconds(firstWatchdogDueMs),
            period: TimeSpan.FromSeconds(_options.WatchdogRepeatSeconds));

        // BUG-N0: Run script on the thread pool and race a hard wall-clock deadline. Roslyn's
        // scripting host may not observe CancellationToken during tight CPU loops; without this,
        // EvaluateAsync never completes and the MCP response never returns.
        var hardDeadlineSeconds = effectiveTimeoutSeconds + _options.WatchdogGraceSeconds;
        var hardDeadline = Task.Delay(TimeSpan.FromSeconds(hardDeadlineSeconds), ct);
        var scriptTask = Task.Run(
            () => CSharpScript.EvaluateAsync<object?>(
                code, scriptOptions, cancellationToken: timeoutCts.Token),
            CancellationToken.None);

        try
        {
            var winner = await Task.WhenAny(scriptTask, hardDeadline).ConfigureAwait(false);
            if (winner == hardDeadline)
            {
                if (hardDeadline.IsCanceled)
                    ct.ThrowIfCancellationRequested();

                sw.Stop();
                try { timeoutCts.Cancel(); }
                catch (ObjectDisposedException) { /* Best-effort cancellation signal for the abandoned script task. */ }

                _logger.LogWarning(
                    "evaluate_csharp: hard deadline ({HardDeadlineSeconds}s = budget {BudgetSeconds}s + grace {GraceSeconds}s) elapsed; returning timeout response. " +
                    "The script may still be running on a thread-pool thread.",
                    hardDeadlineSeconds,
                    effectiveTimeoutSeconds,
                    _options.WatchdogGraceSeconds);

                return FailResult(sw, effectiveTimeoutSeconds, heartbeatCount,
                    $"Script execution was forcibly abandoned after {hardDeadlineSeconds} second(s) (script budget {effectiveTimeoutSeconds}s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS {_options.WatchdogGraceSeconds}s). " +
                    "Roslyn may not cancel tight infinite loops; the server no longer waits for cooperative cancellation.");
            }

            // scriptTask finished first (success, compilation error, runtime error, or cancel).
            return await UnwrapScriptResultAsync(scriptTask, sw, effectiveTimeoutSeconds, heartbeatCount, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref watchdogCompleted, 1);
            try { watchdogTimer?.Dispose(); }
            catch { /* Best-effort */ }

            try
            {
                heartbeatCts.Cancel();
                if (heartbeatTask is not null)
                    await heartbeatTask.ConfigureAwait(false);
            }
            catch { /* Heartbeat cancellation is best-effort */ }
        }
    }

    private async Task<ScriptEvaluationDto> UnwrapScriptResultAsync(
        Task<object?> scriptTask,
        Stopwatch sw,
        int effectiveTimeoutSeconds,
        int heartbeatCount,
        CancellationToken ct)
    {
        try
        {
            var result = await scriptTask.ConfigureAwait(false);
            sw.Stop();
            return new ScriptEvaluationDto(
                Success: true,
                ResultType: result?.GetType().FullName,
                ResultValue: FormatResult(result),
                Error: null,
                CompilationErrors: null,
                ElapsedMs: sw.ElapsedMilliseconds,
                AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
                ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            return FailResult(sw, effectiveTimeoutSeconds, heartbeatCount,
                ex.Message, MapCompilationErrors(ex));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return FailResult(sw, effectiveTimeoutSeconds, heartbeatCount,
                $"Script execution timed out after {effectiveTimeoutSeconds} seconds. Pass timeoutSeconds on the call (UX-002) or set ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS to raise the default.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return FailResult(sw, effectiveTimeoutSeconds, heartbeatCount,
                $"Runtime error: {ex.GetType().Name}: {ex.Message}");
        }
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

    private static ScriptEvaluationDto FailResult(
        Stopwatch sw,
        int timeoutSeconds,
        int heartbeatCount,
        string error,
        List<DiagnosticDto>? compilationErrors = null)
    {
        return new ScriptEvaluationDto(
            Success: false,
            ResultType: null,
            ResultValue: null,
            Error: error,
            CompilationErrors: compilationErrors,
            ElapsedMs: sw.ElapsedMilliseconds,
            AppliedScriptTimeoutSeconds: timeoutSeconds,
            ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null);
    }

    private async Task RunHeartbeatAsync(
        Stopwatch sw,
        TimeSpan budget,
        TimeSpan interval,
        CancellationToken ct,
        Action<ScriptEvaluationProgress> emit)
    {
        var index = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                index++;
                emit(new ScriptEvaluationProgress(sw.Elapsed, budget, index));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when evaluation completes or outer token fires
        }
    }

    private static string? FormatResult(object? result)
    {
        if (result is null) return "null";

        var str = result.ToString();
        if (str is not null && str.Length > 4096)
            str = str[..4093] + "...";

        return str;
    }
}
