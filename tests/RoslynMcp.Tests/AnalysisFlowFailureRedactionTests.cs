using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// analysis-flow-error-detail-redaction: the flow-analysis failure paths in
/// <see cref="FlowAnalysisService"/> and <see cref="ExtractMethodService"/> must never publish
/// raw Roslyn exception text in their client-visible messages. All three catch sites route
/// through <see cref="FlowAnalysisFailurePolicy.CreateFailureMessage"/>, so the redaction
/// contract is asserted directly against the shared policy (the Roslyn analysis APIs offer no
/// injectable seam to force an <see cref="ArgumentException"/> with attacker-controlled text
/// through the live services). Healthy-path DTO behavior is covered by
/// <c>FlowAnalysisServiceTests</c> and <c>ExtractMethodServiceTests</c>, which are unchanged.
/// </summary>
[TestClass]
public sealed class AnalysisFlowFailureRedactionTests
{
    private const string Sentinel = "SENTINEL-SECRET-9F3A";

    [TestMethod]
    [DataRow("Data flow analysis")]
    [DataRow("Control flow analysis")]
    [DataRow("Extract-method data flow analysis")]
    public void CreateFailureMessage_RedactsExceptionTextAndCarriesContract(string operation)
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        var roslynException = new ArgumentException(
            $"statementOrExpression at C:/secret/path/{Sentinel}.cs is not valid");

        var message = FlowAnalysisFailurePolicy.CreateFailureMessage(
            reporter, roslynException, operation, startLine: 12, endLine: 34);

        Assert.IsFalse(message.Contains(Sentinel, StringComparison.Ordinal),
            $"The raw Roslyn exception text must not leak into the published message: {message}");
        Assert.IsFalse(message.Contains(roslynException.Message, StringComparison.Ordinal),
            "No portion of the raw exception message may be republished verbatim.");
        StringAssert.Contains(message, operation, StringComparison.Ordinal);
        StringAssert.Contains(message, "lines 12-34", StringComparison.Ordinal);
        StringAssert.Contains(message, FlowAnalysisFailurePolicy.Remediation, StringComparison.Ordinal);
        StringAssert.Contains(message, "correlationId=", StringComparison.Ordinal);
    }

    [TestMethod]
    public void CreateFailureMessage_ReportsToServerSinkWithFlowAnalysisCategory()
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        var roslynException = new ArgumentException(Sentinel);

        _ = FlowAnalysisFailurePolicy.CreateFailureMessage(
            reporter, roslynException, "Data flow analysis", startLine: 1, endLine: 2);

        Assert.HasCount(1, reporter.Reports);
        Assert.AreSame(roslynException, reporter.Reports[0].Exception,
            "The original exception must reach the server-only diagnostic sink.");
        Assert.AreEqual(UnexpectedExceptionCategory.FlowAnalysis, reporter.Reports[0].Category);
        CollectionAssert.Contains(
            reporter.LastDetails!.Server.ExceptionTypes.ToList(),
            "System.ArgumentException",
            "The server-only projection must retain the exception type topology.");
    }

    [TestMethod]
    public void CreateFailureMessage_NullReporter_FallsBackToUnavailableCorrelationId()
    {
        var message = FlowAnalysisFailurePolicy.CreateFailureMessage(
            exceptionReporter: null,
            new ArgumentException(Sentinel),
            "Control flow analysis",
            startLine: 3,
            endLine: 3);

        Assert.IsFalse(message.Contains(Sentinel, StringComparison.Ordinal));
        StringAssert.Contains(message, "correlationId=unavailable", StringComparison.Ordinal);
    }

    /// <summary>
    /// Recording <see cref="IUnexpectedExceptionReporter"/> double: captures each report and
    /// delegates the projection to the shared <see cref="PublicExceptionDetailPolicy"/>.
    /// </summary>
    private sealed class RecordingUnexpectedExceptionReporter : IUnexpectedExceptionReporter
    {
        public List<(Exception Exception, UnexpectedExceptionCategory Category)> Reports { get; } = [];

        public UnexpectedExceptionDetails? LastDetails { get; private set; }

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            Reports.Add((exception, category));
            LastDetails = PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId: null);
            return LastDetails;
        }
    }
}
