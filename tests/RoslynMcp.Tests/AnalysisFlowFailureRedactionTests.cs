using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// analysis-flow-error-detail-redaction: the flow-analysis failure paths in
/// <see cref="FlowAnalysisService"/> and <see cref="ExtractMethodService"/> must never publish
/// raw Roslyn exception text in their client-visible messages. All three catch sites route
/// through <see cref="FlowAnalysisFailurePolicy.CreateFailure"/>, so the redaction
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
    [DataRow(nameof(FlowAnalysisOperation.DataFlow), "Data flow analysis", "complete expression-bodied member")]
    [DataRow(nameof(FlowAnalysisOperation.ControlFlow), "Control flow analysis", "complete expression-bodied member")]
    [DataRow(nameof(FlowAnalysisOperation.ExtractMethod), "Extract-method data flow analysis", "complete statements within a single block")]
    public void CreateFailure_RedactsExceptionTextAndCarriesContract(
        string operationName, string label, string correction)
    {
        var operation = Enum.Parse<FlowAnalysisOperation>(operationName);
        var reporter = new RecordingUnexpectedExceptionReporter();
        var roslynException = new ArgumentException(
            $"statementOrExpression at C:/secret/path/{Sentinel}.cs is not valid");

        var failure = FlowAnalysisFailurePolicy.CreateFailure(
            reporter, roslynException, operation, startLine: 12, endLine: 34);
        var message = failure.PublicMessage;
        Assert.AreSame(roslynException, failure.InnerException);

        Assert.IsFalse(message.Contains(Sentinel, StringComparison.Ordinal),
            $"The raw Roslyn exception text must not leak into the published message: {message}");
        Assert.IsFalse(message.Contains(roslynException.Message, StringComparison.Ordinal),
            "No portion of the raw exception message may be republished verbatim.");
        StringAssert.Contains(message, label, StringComparison.Ordinal);
        StringAssert.Contains(message, "lines 12-34", StringComparison.Ordinal);
        StringAssert.Contains(message, correction, StringComparison.Ordinal);
        if (operation == FlowAnalysisOperation.ExtractMethod)
        {
            StringAssert.Contains(message, "complete statements within a single block", StringComparison.Ordinal);
            Assert.IsFalse(message.Contains("expression-bodied member", StringComparison.Ordinal),
                "An expression-bodied member is not a valid extract-method selection.");
        }
        StringAssert.Contains(message, "correlationId=", StringComparison.Ordinal);
        using var envelope = System.Text.Json.JsonDocument.Parse(
            RoslynMcp.Host.Stdio.Tools.ToolErrorHandler.ClassifyAndFormat(failure, "flow_failure_probe"));
        Assert.AreEqual("InvalidOperation", envelope.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("InvalidOperationException", envelope.RootElement.GetProperty("exceptionType").GetString());
        Assert.AreEqual(message, envelope.RootElement.GetProperty("message").GetString());
        Assert.IsFalse(envelope.RootElement.GetRawText().Contains(Sentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CreateFailure_ReportsToServerSinkWithFlowAnalysisCategory()
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        var roslynException = new ArgumentException(Sentinel);

        _ = FlowAnalysisFailurePolicy.CreateFailure(
            reporter, roslynException, FlowAnalysisOperation.DataFlow, startLine: 1, endLine: 2);

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
    public void CreateFailure_NullReporter_FallsBackToUnavailableCorrelationId()
    {
        var failure = FlowAnalysisFailurePolicy.CreateFailure(
            exceptionReporter: null,
            new ArgumentException(Sentinel),
            FlowAnalysisOperation.ControlFlow,
            startLine: 3,
            endLine: 3);

        var message = failure.PublicMessage;
        Assert.IsFalse(message.Contains(Sentinel, StringComparison.Ordinal));
        StringAssert.Contains(message, "correlationId=unavailable", StringComparison.Ordinal);
    }

    [TestMethod]
    public void CreateFailure_UnknownOperationRefusesBeforeReporting()
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => FlowAnalysisFailurePolicy.CreateFailure(
            reporter, new ArgumentException(Sentinel), (FlowAnalysisOperation)int.MaxValue, 1, 2));
        Assert.IsFalse(exception is IPublicMessageException);
        Assert.IsEmpty(reporter.Reports);
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
