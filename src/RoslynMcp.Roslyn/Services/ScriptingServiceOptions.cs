namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <see cref="ScriptingService"/> timeout and diagnostic behaviour.
/// </summary>
public sealed record ScriptingServiceOptions
{
    /// <summary>
    /// Largest whole-second duration supported by the runtime timer APIs.
    /// The effective script budget plus watchdog grace must not exceed this value.
    /// </summary>
    public const int MaxTimerDurationSeconds = 4_294_967;

    /// <summary>
    /// Default script evaluation timeout in seconds. Defaults to 10. The timeout plus
    /// <see cref="WatchdogGraceSeconds"/> must not exceed <see cref="MaxTimerDurationSeconds"/>.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Interval between progress heartbeat ticks in milliseconds. Defaults to 2000.
    /// </summary>
    public int HeartbeatIntervalMs { get; init; } = 2000;

    /// <summary>
    /// Seconds of elapsed evaluation before a "still running" warning is emitted. Defaults to 5.
    /// </summary>
    public int StuckWarningSeconds { get; init; } = 5;

    /// <summary>
    /// Seconds after <see cref="TimeoutSeconds"/> before the host terminates the isolated
    /// worker process. Roslyn may not honor <see cref="System.Threading.CancellationToken"/>
    /// during tight loops; this is the hard wall-clock cap that always fires. Defaults to 10.
    /// </summary>
    public int WatchdogGraceSeconds { get; init; } = 10;

    /// <summary>
    /// Legacy no-op retained for source and binary compatibility. The watchdog deadline is
    /// single-shot and has no repeated-log interval.
    /// </summary>
    public int WatchdogRepeatSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of script evaluations allowed to be racing to deadline at once.
    /// Each evaluation runs in an owned child process with a dedicated parent-side monitor.
    /// The slot is released only after normal exit or bounded termination cleanup. Defaults to 4.
    /// </summary>
    public int MaxConcurrentEvaluations { get; init; } = 4;

    /// <summary>
    /// How long a new evaluation will wait to acquire a concurrency slot before
    /// returning the at-capacity error. Defaults to 5 seconds.
    /// </summary>
    public int ConcurrencySlotAcquireTimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Hard upper bound on worker processes that the operating system failed to terminate
    /// across the lifetime of the host. Once exceeded, new <c>evaluate_csharp</c> requests
    /// fail fast with an actionable error directing the operator to restart the host.
    /// Defaults to 8 — well above the in-flight cap but low enough to fail closed when
    /// process cleanup is persistently unavailable.
    /// </summary>
    public int MaxAbandonedEvaluations { get; init; } = 8;
}
