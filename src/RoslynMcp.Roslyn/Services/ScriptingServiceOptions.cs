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
    /// Seconds after <see cref="TimeoutSeconds"/> before the watchdog first logs at Critical
    /// if evaluation is still running. Roslyn may not honor cancellation during some compilation
    /// phases; this detects that class of failure. Defaults to 10.
    /// </summary>
    public int WatchdogGraceSeconds { get; init; } = 10;

    /// <summary>
    /// Interval between repeated Critical watchdog logs while evaluation exceeds budget + grace.
    /// Defaults to 60.
    /// </summary>
    public int WatchdogRepeatSeconds { get; init; } = 60;
}
