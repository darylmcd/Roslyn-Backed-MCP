using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

internal enum FlowAnalysisOperation { DataFlow, ControlFlow, ExtractMethod }

/// <summary>
/// Creates secret-safe public failures with operation-specific selection guidance.
/// The raw Roslyn exception text (message, data, source paths, stack text) is never copied
/// into the client-visible message; full detail stays in the server-only diagnostic sink
/// keyed by the correlation ID.
/// </summary>
internal static class FlowAnalysisFailurePolicy
{
    private const string FlowRemediation =
        "Try widening the range to a complete expression-bodied member or to statements " +
        "within a single block (avoid spanning try/catch/finally boundaries).";
    private const string ExtractionRemediation =
        "Ensure the selection covers complete statements within a single block " +
        "(avoid spanning try/catch/finally boundaries).";

    public static PublicInvalidOperationException CreateFailure(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception,
        FlowAnalysisOperation operation,
        int startLine,
        int endLine)
    {
        var (label, remediation) = operation switch
        {
            FlowAnalysisOperation.DataFlow => ("Data flow analysis", FlowRemediation),
            FlowAnalysisOperation.ControlFlow => ("Control flow analysis", FlowRemediation),
            FlowAnalysisOperation.ExtractMethod => ("Extract-method data flow analysis", ExtractionRemediation),
            _ => throw InvalidOperationErrors.Internal("Unsupported flow-analysis failure operation."),
        };
        var detail = UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.FlowAnalysis).Public;
        return new PublicInvalidOperationException(
            $"{label} failed for lines {startLine}-{endLine}. " +
            $"{remediation} {PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(detail.CorrelationId)}", exception);
    }
}
