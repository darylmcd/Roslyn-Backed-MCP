using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Ambient-metrics recording helpers extracted from <see cref="StructuredCallToolFilter"/>.
/// Each method writes one field on the current <see cref="AmbientGateMetrics"/> request scope
/// (a no-op when no scope is active), keeping the filter's dispatch body free of the
/// null-check ceremony. Purely a write-through to <see cref="AmbientGateMetrics.Current"/>;
/// holds no state of its own.
/// </summary>
internal static class CallMetricsRecorder
{
    /// <summary>
    /// Records the workspaceId auto-resolution outcome (<c>explicit</c>, <c>single-workspace</c>,
    /// <c>fast-fail</c>, <c>auto-loaded</c>, or <c>request-state</c>).
    /// </summary>
    public static void RecordAutoResolution(string value)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.AutoResolution = value;
        }
    }

    /// <summary>Records the elapsed time spent auto-loading a workspace on demand.</summary>
    public static void RecordAutoLoadElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.AutoLoadElapsedMs = elapsedMs;
        }
    }

    /// <summary>Records the end-to-end wall-clock elapsed time for the tool call.</summary>
    public static void RecordElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.ElapsedMs = elapsedMs;
        }
    }
}
