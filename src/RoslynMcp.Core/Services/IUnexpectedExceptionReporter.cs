namespace RoslynMcp.Core.Services;

/// <summary>
/// Stable server-diagnostic categories for unexpected failures that are recovered into public
/// result DTOs instead of escaping through the shared tool-call boundary.
/// </summary>
public enum UnexpectedExceptionCategory
{
    ToolCall,
    TestCoverage,
    Scaffolding,
    WorkspaceValidation,
    AnalyzerLoad,
    BulkReference,
    CompositeApply,
    FixAll,
    WorkspaceReadiness,
    WorkspaceCloseProcessDrain,
    GetPrompt,
    FlowAnalysis,
    TestRun,
    ServerProcessMetadata,
    AnalysisScan,
}

/// <summary>
/// Projects and reports an unexpected exception without exposing its message, data, source path,
/// or raw stack text. Implementations may publish the server-only projection to an opt-in sink.
/// </summary>
public interface IUnexpectedExceptionReporter
{
    UnexpectedExceptionDetails ReportUnexpected(
        Exception exception,
        UnexpectedExceptionCategory category);
}

/// <summary>
/// Shared optional-reporter adapter. Direct service construction still receives the same safe
/// public projection when no server reporter is available, while the production DI graph supplies
/// the configured reporter and correlation-aware sink.
/// </summary>
public static class UnexpectedExceptionReporting
{
    public static UnexpectedExceptionDetails Report(
        IUnexpectedExceptionReporter? reporter,
        Exception exception,
        UnexpectedExceptionCategory category) =>
        reporter?.ReportUnexpected(exception, category)
        ?? PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId: null);
}
