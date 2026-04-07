namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Configuration options for <see cref="ScriptingService"/> timeout and diagnostic behaviour.
/// </summary>
public sealed record ScriptingServiceOptions
{
    /// <summary>
    /// Default script evaluation timeout in seconds. Defaults to 10.
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
    /// Seconds after <see cref="TimeoutSeconds"/> before the host abandons the script worker
    /// and returns a forcibly-abandoned timeout response. Roslyn may not honor
    /// <see cref="System.Threading.CancellationToken"/> during tight loops; this is the
    /// hard wall-clock cap that always fires. Defaults to 10.
    /// </summary>
    public int WatchdogGraceSeconds { get; init; } = 10;

    /// <summary>
    /// Interval between repeated Critical watchdog logs while evaluation exceeds budget + grace.
    /// Defaults to 60. Retained for log-throttling consumers; the deadline itself is single-shot.
    /// </summary>
    public int WatchdogRepeatSeconds { get; init; } = 60;

    /// <summary>
    /// FLAG-5C: maximum number of script evaluations allowed to be racing to deadline at once.
    /// Each evaluation runs on its own dedicated background thread. The slot is released as
    /// soon as the request completes (success, error, or hard deadline) so a leaked script
    /// thread does NOT keep the slot — it transitions to the abandoned-thread bookkeeping
    /// (<see cref="MaxAbandonedEvaluations"/>). Defaults to 4.
    /// </summary>
    public int MaxConcurrentEvaluations { get; init; } = 4;

    /// <summary>
    /// FLAG-5C: how long a new evaluation will wait to acquire a concurrency slot before
    /// returning the at-capacity error. Defaults to 5 seconds.
    /// </summary>
    public int ConcurrencySlotAcquireTimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// FLAG-5C: hard upper bound on how many leaked script worker threads we will tolerate
    /// across the lifetime of the host. Once exceeded, new <c>evaluate_csharp</c> requests
    /// fail fast with an actionable error directing the operator to restart the host.
    /// Defaults to 8 — well above the in-flight cap so transient deadline races do not
    /// trigger it, but low enough to fail fast on a persistent infinite-loop pattern.
    /// </summary>
    public int MaxAbandonedEvaluations { get; init; } = 8;
}
