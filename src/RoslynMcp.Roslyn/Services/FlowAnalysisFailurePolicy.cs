using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Creates one shared secret-safe failure message for recovered flow-analysis errors.
/// The raw Roslyn exception text (message, data, source paths, stack text) is never copied
/// into the client-visible message; full detail stays in the server-only diagnostic sink
/// keyed by the correlation ID.
/// </summary>
internal static class FlowAnalysisFailurePolicy
{
    internal const string Remediation =
        "Try widening the range to a complete expression-bodied member or to statements " +
        "within a single block (avoid spanning try/catch/finally boundaries).";

    public static string CreateFailureMessage(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception,
        string operation,
        int startLine,
        int endLine)
    {
        var detail = UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.FlowAnalysis).Public;
        return $"{operation} failed for lines {startLine}-{endLine}. " +
            $"{Remediation} correlationId={detail.CorrelationId}";
    }
}
