using System.Globalization;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Configuration;

/// <summary>
/// Binds and validates script-execution timing and capacity settings before the host starts.
/// The value-reader seam keeps environment parsing deterministic and secret-safe in tests.
/// </summary>
internal static class ScriptingOptionsEnvironmentBinder
{
    internal const string TimeoutVariable = "ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS";
    internal const string HeartbeatVariable = "ROSLYNMCP_SCRIPT_HEARTBEAT_MS";
    internal const string StuckWarningVariable = "ROSLYNMCP_SCRIPT_STUCK_WARNING_SECONDS";
    internal const string WatchdogGraceVariable = "ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS";
    internal const string MaxConcurrentVariable = "ROSLYNMCP_SCRIPT_MAX_CONCURRENT";
    internal const string SlotWaitVariable = "ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS";
    internal const string MaxAbandonedVariable = "ROSLYNMCP_SCRIPT_MAX_ABANDONED";

    internal static ScriptingServiceOptions Bind(Func<string, string?> readValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        var defaults = new ScriptingServiceOptions();
        var timeout = ReadInteger(
            readValue,
            TimeoutVariable,
            defaults.TimeoutSeconds,
            minimum: 1,
            maximum: ScriptingServiceOptions.MaxTimerDurationSeconds);
        var watchdogGrace = ReadInteger(
            readValue,
            WatchdogGraceVariable,
            defaults.WatchdogGraceSeconds,
            minimum: 0,
            maximum: ScriptingServiceOptions.MaxTimerDurationSeconds);

        if ((long)timeout + watchdogGrace > ScriptingServiceOptions.MaxTimerDurationSeconds)
        {
            throw new InvalidOperationException(
                $"{TimeoutVariable} plus {WatchdogGraceVariable} must not exceed " +
                $"{ScriptingServiceOptions.MaxTimerDurationSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        return defaults with
        {
            TimeoutSeconds = timeout,
            HeartbeatIntervalMs = ReadInteger(
                readValue,
                HeartbeatVariable,
                defaults.HeartbeatIntervalMs,
                minimum: 1),
            StuckWarningSeconds = ReadInteger(
                readValue,
                StuckWarningVariable,
                defaults.StuckWarningSeconds,
                minimum: 1),
            WatchdogGraceSeconds = watchdogGrace,
            MaxConcurrentEvaluations = ReadInteger(
                readValue,
                MaxConcurrentVariable,
                defaults.MaxConcurrentEvaluations,
                minimum: 1),
            ConcurrencySlotAcquireTimeoutSeconds = ReadInteger(
                readValue,
                SlotWaitVariable,
                defaults.ConcurrencySlotAcquireTimeoutSeconds,
                minimum: 1),
            MaxAbandonedEvaluations = ReadInteger(
                readValue,
                MaxAbandonedVariable,
                defaults.MaxAbandonedEvaluations,
                minimum: 1),
        };
    }

    private static int ReadInteger(
        Func<string, string?> readValue,
        string variableName,
        int fallback,
        int minimum,
        int maximum = int.MaxValue)
    {
        var value = readValue(variableName);
        if (IsUnset(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new InvalidOperationException(
                $"{variableName} must be an integer between " +
                $"{minimum.ToString(CultureInfo.InvariantCulture)} and " +
                $"{maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }

    private static bool IsUnset(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        (value.StartsWith("${user_config.", StringComparison.Ordinal) &&
         value.EndsWith('}'));
}
