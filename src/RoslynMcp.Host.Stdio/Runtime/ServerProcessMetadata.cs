using System.ComponentModel;
using System.Diagnostics;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Runtime;

/// <summary>
/// Captures process identity metadata once for the host lifetime so every public and internal
/// consumer observes the same start timestamp.
/// </summary>
public sealed class ServerProcessMetadata
{
    public ServerProcessMetadata(IUnexpectedExceptionReporter? exceptionReporter = null)
        : this(ReadProcessStartUtc, () => TimeProvider.System.GetUtcNow(), exceptionReporter)
    {
    }

    internal ServerProcessMetadata(
        Func<DateTimeOffset> readProcessStartUtc,
        Func<DateTimeOffset> readWallClockUtc,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        ArgumentNullException.ThrowIfNull(readProcessStartUtc);
        ArgumentNullException.ThrowIfNull(readWallClockUtc);

        try
        {
            StartedAtUtc = readProcessStartUtc().ToUniversalTime();
        }
        catch (Exception ex) when (IsExpectedStartTimeFailure(ex))
        {
            UnexpectedExceptionReporting.Report(
                exceptionReporter,
                ex,
                UnexpectedExceptionCategory.ServerProcessMetadata);
            StartedAtUtc = readWallClockUtc().ToUniversalTime();
            UsedWallClockFallback = true;
        }
    }

    public DateTimeOffset StartedAtUtc { get; }

    internal bool UsedWallClockFallback { get; }

    private static DateTimeOffset ReadProcessStartUtc() =>
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static bool IsExpectedStartTimeFailure(Exception exception) =>
        exception is InvalidOperationException or NotSupportedException or Win32Exception;
}
