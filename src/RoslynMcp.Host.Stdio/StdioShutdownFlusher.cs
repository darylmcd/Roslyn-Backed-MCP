namespace RoslynMcp.Host.Stdio;

/// <summary>
/// Flushes the MCP stdout transport without allowing shutdown-time transport failures
/// to escape. Failures are reported only through the supplied diagnostic writer so
/// protocol stdout never receives operational text.
/// </summary>
internal static class StdioShutdownFlusher
{
    public static void Flush(TextWriter output, Action<string> reportDiagnostic, string phase)
    {
        try
        {
            output.Flush();
        }
        catch (Exception ex)
        {
            ReportFailure(reportDiagnostic, phase, ex);
        }
    }

    public static async Task FlushAsync(
        TextWriter output,
        Action<string> reportDiagnostic,
        string phase)
    {
        try
        {
            await output.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportFailure(reportDiagnostic, phase, ex);
        }
    }

    private static void ReportFailure(
        Action<string> reportDiagnostic,
        string phase,
        Exception exception)
    {
        var message =
            $"[roslyn-mcp] stdout flush failed during {phase} ({exception.GetType().Name}).";
        try
        {
            reportDiagnostic(message);
        }
        catch (Exception diagnosticsException)
        {
            System.Diagnostics.Trace.TraceError(
                "{0} stderr reporting also failed ({1}).",
                message,
                diagnosticsException.GetType().Name);
        }
    }
}
