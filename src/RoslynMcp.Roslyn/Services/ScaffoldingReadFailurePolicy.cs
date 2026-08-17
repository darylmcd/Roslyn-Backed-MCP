using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Creates one shared secret-safe warning shape for recovered scaffolding reads.</summary>
internal static class ScaffoldingReadFailurePolicy
{
    public static string CreateWarning(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception,
        string affectedInput)
    {
        var detail = UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.Scaffolding).Public;
        return $"Could not read the {affectedInput}; scaffolded without pattern inference. " +
            $"correlationId={detail.CorrelationId}";
    }
}
