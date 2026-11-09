namespace RoslynMcp.Core.Services;

/// <summary>Identifies an owned timeout source without carrying caller input.</summary>
public enum OperationTimeoutTimer
{
    Request,
    Build,
    Test,
    VulnerabilityScan,
    ForkRestore,
}

/// <summary>A timeout with a known timer and configuration source.</summary>
public sealed class OperationTimeoutException : TimeoutException
{
    public OperationTimeoutTimer Timer { get; }
    public string EnvironmentKnob { get; }
    public TimeSpan ConfiguredDuration { get; }
    public TimeSpan ElapsedDuration { get; }

    public OperationTimeoutException(
        OperationTimeoutTimer timer,
        TimeSpan configuredDuration,
        TimeSpan elapsedDuration,
        Exception innerException)
        : base("The operation timed out.", innerException)
    {
        if (configuredDuration <= TimeSpan.Zero) throw new PublicArgumentOutOfRangeException("The configured timeout must be positive.", nameof(configuredDuration));
        if (elapsedDuration < TimeSpan.Zero) throw new PublicArgumentOutOfRangeException("The elapsed duration cannot be negative.", nameof(elapsedDuration));

        Timer = timer;
        EnvironmentKnob = timer switch
        {
            OperationTimeoutTimer.Request => "ROSLYNMCP_REQUEST_TIMEOUT_SECONDS",
            OperationTimeoutTimer.Build => "ROSLYNMCP_BUILD_TIMEOUT_SECONDS",
            OperationTimeoutTimer.Test => "ROSLYNMCP_TEST_TIMEOUT_SECONDS",
            OperationTimeoutTimer.VulnerabilityScan => "ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS",
            OperationTimeoutTimer.ForkRestore => "ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES",
            _ => throw new PublicArgumentOutOfRangeException("The timeout timer is unknown.", nameof(timer)),
        };
        ConfiguredDuration = configuredDuration;
        ElapsedDuration = elapsedDuration;
    }
}
