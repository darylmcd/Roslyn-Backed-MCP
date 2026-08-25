using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Roslyn C# scripting host with hard wall-clock termination guarantees.
///
/// FLAG-5C: <c>Microsoft.CodeAnalysis.CSharp.Scripting</c> does not honor
/// <see cref="CancellationToken"/> for tight CPU loops (e.g. <c>while(true){}</c>).
/// <see cref="ScriptExecutionSupervisor"/> runs every script in an owned child process.
/// A dedicated parent-side monitor enforces the hard deadline without relying on a
/// thread-pool callback and reclaims the process before returning capacity.
/// </summary>
public sealed class ScriptingService : IScriptingService
{
    private readonly ScriptingServiceOptions _options;
    private readonly ScriptExecutionSupervisor _executionSupervisor;
    private readonly Func<string, ScriptOptions, CancellationToken, ScriptExecutionOutcome>? _executeScript;

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
        : this(logger, options, new ScriptWorkerProcess(logger))
    {
    }

    internal ScriptingService(
        ILogger<ScriptingService> logger,
        ScriptingServiceOptions options,
        IScriptWorkerProcess workerProcess)
    {
        _options = options;
        _executionSupervisor = new ScriptExecutionSupervisor(
            logger,
            options,
            workerProcess ?? throw new ArgumentNullException(nameof(workerProcess)));
    }

    internal ScriptingService(
        ILogger<ScriptingService> logger,
        ScriptingServiceOptions options,
        Func<string, ScriptOptions, CancellationToken, ScriptExecutionOutcome> executeScript)
    {
        _options = options;
        _executionSupervisor = new ScriptExecutionSupervisor(logger, options);
        _executeScript = executeScript ?? throw new ArgumentNullException(nameof(executeScript));
    }

    internal long ActiveEvaluationCount => _executionSupervisor.ActiveEvaluationCount;
    internal long AbandonedEvaluationCount => _executionSupervisor.AbandonedEvaluationCount;

    public async Task<ScriptEvaluationDto> EvaluateAsync(
        string code,
        string[]? imports,
        CancellationToken ct,
        Action<ScriptEvaluationProgress>? onProgress = null,
        int? timeoutSecondsOverride = null)
    {
        var settings = CreateExecutionSettings(timeoutSecondsOverride);
        var result = _executeScript is null
            ? await _executionSupervisor.ExecuteAsync(
                new ScriptWorkerRequest(code, imports, settings.EffectiveTimeoutSeconds),
                onProgress,
                settings,
                ct).ConfigureAwait(false)
            : await _executionSupervisor.ExecuteAsync(
                timeoutToken => _executeScript(code, BuildScriptOptions(imports), timeoutToken),
                onProgress,
                settings,
                ct).ConfigureAwait(false);

        return BuildEvaluationDto(result, ct);
    }

    private ScriptExecutionSupervisorSettings CreateExecutionSettings(int? timeoutSecondsOverride)
    {
        // UX-002: a supplied per-call timeout is an explicit contract, not a fallback hint.
        if (timeoutSecondsOverride is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSecondsOverride),
                timeoutSecondsOverride,
                "Script timeout must be greater than zero when supplied.");
        }

        var effectiveTimeoutSeconds = timeoutSecondsOverride ?? _options.TimeoutSeconds;
        var graceSeconds = Math.Max(0, _options.WatchdogGraceSeconds);
        var maximumBudgetSeconds = ScriptingServiceOptions.MaxTimerDurationSeconds - graceSeconds;
        if (maximumBudgetSeconds < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(ScriptingServiceOptions.WatchdogGraceSeconds)} must be less than " +
                $"{ScriptingServiceOptions.MaxTimerDurationSeconds} seconds.");
        }

        if (effectiveTimeoutSeconds is < 1 || effectiveTimeoutSeconds > maximumBudgetSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSecondsOverride),
                effectiveTimeoutSeconds,
                $"Script timeout must be between 1 and {maximumBudgetSeconds} seconds after watchdog grace.");
        }

        return new ScriptExecutionSupervisorSettings(
            EffectiveTimeoutSeconds: effectiveTimeoutSeconds,
            GraceSeconds: graceSeconds,
            HardDeadlineSeconds: checked(effectiveTimeoutSeconds + graceSeconds),
            HeartbeatInterval: TimeSpan.FromMilliseconds(Math.Max(100, _options.HeartbeatIntervalMs)),
            Budget: TimeSpan.FromSeconds(effectiveTimeoutSeconds));
    }

    private ScriptEvaluationDto BuildEvaluationDto(ScriptExecutionResult result, CancellationToken ct)
    {
        if (result.CapacityFailure.HasValue)
        {
            return BuildCapacityFailureDto(result.CapacityFailure.Value, result.Settings.EffectiveTimeoutSeconds);
        }

        if (result.Outcome.Kind == ScriptExecutionOutcomeKind.OuterCancelled)
        {
            ct.ThrowIfCancellationRequested();
        }

        if (result.Outcome.Kind == ScriptExecutionOutcomeKind.HardDeadline)
        {
            return BuildHardDeadlineDto(result);
        }

        return BuildOutcomeDto(result.Outcome, result.ElapsedMs, result.HeartbeatCount, result.Settings.EffectiveTimeoutSeconds);
    }

    private ScriptEvaluationDto BuildCapacityFailureDto(
        ScriptExecutionCapacityFailure failure,
        int effectiveTimeoutSeconds)
    {
        var error = failure.Kind switch
        {
            ScriptExecutionCapacityFailureKind.AbandonedWorkerCap =>
                $"evaluate_csharp has {failure.AbandonedCount} unreclaimed worker processes (cap {_options.MaxAbandonedEvaluations}). " +
                "Likely caused by an operating-system process termination failure. " +
                "Restart the MCP host to recover.",
            ScriptExecutionCapacityFailureKind.ConcurrentSlotCap =>
                $"evaluate_csharp is at capacity ({_options.MaxConcurrentEvaluations} concurrent slots, {failure.ActiveCount} in flight). " +
                "Wait briefly or raise ROSLYNMCP_SCRIPT_MAX_CONCURRENT.",
            _ => "evaluate_csharp capacity check failed.",
        };

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

    private ScriptEvaluationDto BuildHardDeadlineDto(ScriptExecutionResult result)
    {
        return new ScriptEvaluationDto(
            Success: false,
            ResultType: null,
            ResultValue: null,
            Error: $"Script execution reached its hard deadline after {result.Settings.HardDeadlineSeconds} second(s) " +
                   $"(script budget {result.Settings.EffectiveTimeoutSeconds}s + ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS {result.Settings.GraceSeconds}s). " +
                   "The isolated worker process was terminated so CPU and concurrency capacity were reclaimed. " +
                   $"{result.AbandonedCount}/{result.MaxAbandonedCount} worker process(es) could not be reclaimed.",
            CompilationErrors: null,
            ElapsedMs: result.ElapsedMs,
            AppliedScriptTimeoutSeconds: result.Settings.EffectiveTimeoutSeconds,
            ProgressHeartbeatCount: result.HeartbeatCount > 0 ? result.HeartbeatCount : null);
    }

    internal static ScriptExecutionOutcome ExecuteScript(
        string code,
        ScriptOptions scriptOptions,
        CancellationToken timeoutToken)
    {
        try
        {
            // APPROVED-SYNC-OVER-ASYNC: ScriptExecutionSupervisor invokes this method on a
            // dedicated worker thread. The fence is intentional because CSharpScript can ignore
            // cancellation in tight loops; never move it onto a thread-pool or request thread.
            var result = CSharpScript
                .EvaluateAsync<object?>(code, scriptOptions, cancellationToken: timeoutToken)
                .GetAwaiter().GetResult();
            return ScriptExecutionOutcome.Success(result);
        }
        catch (CompilationErrorException cex)
        {
            return ScriptExecutionOutcome.CompilationFailure(cex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ScriptExecutionOutcome.Runtime(ex);
        }
    }

    /// <summary>
    /// Translates a worker-thread <see cref="ScriptExecutionOutcome"/> into a <see cref="ScriptEvaluationDto"/>.
    /// All branches share elapsed-time, applied-timeout, and heartbeat fields; only the success
    /// flag, payload, and error message change per outcome kind.
    /// </summary>
    private static ScriptEvaluationDto BuildOutcomeDto(
        ScriptExecutionOutcome outcome,
        long elapsedMs,
        int heartbeatCount,
        int effectiveTimeoutSeconds)
    {
        var (success, resultType, resultValue, error, compilationErrors) = outcome.Kind switch
        {
            ScriptExecutionOutcomeKind.Success => (
                true,
                outcome.ResultType,
                outcome.ResultValue,
                (string?)null,
                (List<DiagnosticDto>?)null),
            ScriptExecutionOutcomeKind.CompilationFailure => (
                false,
                (string?)null,
                (string?)null,
                outcome.Error,
                outcome.CompilationErrors),
            ScriptExecutionOutcomeKind.TimedOut => (
                false,
                (string?)null,
                (string?)null,
                $"Script execution timed out after {effectiveTimeoutSeconds} seconds. " +
                "Pass timeoutSeconds on the call (UX-002) or set ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS to raise the default.",
                (List<DiagnosticDto>?)null),
            ScriptExecutionOutcomeKind.Runtime => (
                false,
                (string?)null,
                (string?)null,
                outcome.Error,
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
            ElapsedMs: elapsedMs,
            AppliedScriptTimeoutSeconds: effectiveTimeoutSeconds,
            ProgressHeartbeatCount: heartbeatCount > 0 ? heartbeatCount : null);
    }

    internal static ScriptOptions BuildScriptOptions(string[]? imports)
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

    internal static List<DiagnosticDto> MapCompilationErrors(CompilationErrorException ex)
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
                    EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null,
                    Location: null);
            })
            .ToList();
    }

    internal static string? FormatResult(object? result)
    {
        if (result is null) return "null";

        var str = result.ToString();
        if (str is not null && str.Length > 4096)
            str = str[..4093] + "...";

        return str;
    }
}
