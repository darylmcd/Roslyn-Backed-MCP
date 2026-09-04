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
            PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(detail.CorrelationId);
    }

    /// <summary>
    /// Secret-safe diagnostic for partial sibling test-name discovery. The warning deliberately
    /// omits the failed file identity and count because readable siblings remain usable.
    /// </summary>
    public static string CreateSiblingNameDiscoveryWarning(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception)
    {
        var detail = UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.Scaffolding).Public;
        return "Sibling test-name discovery was incomplete; sampled naming used the readable siblings. " +
            PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(detail.CorrelationId);
    }

    /// <summary>
    /// Secret-safe diagnostic for a recovered project-file parse failure. Carries only the
    /// redacted project identity (file name, never the full path), the deterministic outcome
    /// text, and the correlation ID from the shared unexpected-exception projection.
    /// </summary>
    public static string CreateProjectParseDiagnostic(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception,
        string? projectFilePath,
        string outcome)
    {
        var detail = UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.Scaffolding).Public;
        var projectFileName = string.IsNullOrWhiteSpace(projectFilePath)
            ? "unknown"
            : Path.GetFileName(projectFilePath);
        return $"Failed to parse project file '{projectFileName}'; {outcome}. " +
            PublicExceptionDetailPolicy.FormatCorrelationIdSuffix(detail.CorrelationId);
    }
}
