using System.ComponentModel;
using System.Diagnostics;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Host.Stdio.Runtime;

/// <summary>
/// Captures process identity metadata once for the host lifetime so every public and internal
/// consumer observes the same start timestamp. Also owns the consume-once previous-process
/// snapshot (<see cref="PreviousProcessSnapshot"/>) that <c>server_info</c> and
/// <c>server_heartbeat</c> drain through this DI-injected instance.
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

    /// <summary>
    /// <c>host-recycle-opacity</c>: the previous host process's exit record, published once by
    /// <c>Program.cs</c> at startup and surfaced exactly once on the first
    /// <c>server_info</c> / <c>server_heartbeat</c> probe. Per-instance state — a snapshot
    /// published on one <see cref="ServerProcessMetadata"/> is never visible to another.
    /// </summary>
    internal HostProcessMetadataSnapshotProvider PreviousProcessSnapshot { get; } = new();

    private static DateTimeOffset ReadProcessStartUtc() =>
        Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private static bool IsExpectedStartTimeFailure(Exception exception) =>
        exception is InvalidOperationException or NotSupportedException or Win32Exception;
}
