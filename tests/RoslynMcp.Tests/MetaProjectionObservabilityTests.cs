using System.Text.Json;
using ModelContextProtocol.Protocol;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Middleware;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class MetaProjectionObservabilityTests
{
    [TestMethod]
    public void InjectMetaIfPossible_UnexpectedProjectionFailure_ReturnsOriginalAndReportsSafely()
    {
        const string original = "{\"secret\":\"must-not-be-projected\"}";
        var reporter = new RecordingUnexpectedExceptionReporter();
        using var scope = AmbientGateMetrics.BeginRequest();

        var result = ToolErrorHandler.InjectMetaIfPossible(
            original,
            "sample_tool",
            reporter,
            _ => throw new InvalidOperationException("sensitive exception detail"));

        Assert.AreEqual(original, result, "Projection failure must never alter the MCP result.");
        Assert.AreEqual(1, reporter.ReportCount);
        Assert.AreEqual(UnexpectedExceptionCategory.MetaProjection, reporter.Category);
        Assert.IsFalse(reporter.ServerDiagnostic!.ToString()!.Contains("sensitive", StringComparison.Ordinal));
        Assert.IsFalse(reporter.ServerDiagnostic.ToString()!.Contains("must-not-be-projected", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("[1,2,3]")]
    public void InjectMetaIfPossible_NormalPassThrough_DoesNotReport(string original)
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        using var scope = AmbientGateMetrics.BeginRequest();

        var result = ToolErrorHandler.InjectMetaIfPossible(original, "sample_tool", reporter);

        Assert.AreEqual(original, result);
        Assert.AreEqual(0, reporter.ReportCount);
    }

    [TestMethod]
    public void InjectMetaIfPossible_ProducerMetaObject_PreservesProducerAndNestsGateMetrics()
    {
        const string original = "{\"value\":1,\"_meta\":{\"producer\":{\"sentinel\":true}}}";
        var reporter = new RecordingUnexpectedExceptionReporter();
        using var scope = AmbientGateMetrics.BeginRequest();

        var result = ToolErrorHandler.InjectMetaIfPossible(original, "sample_tool", reporter);

        using var document = JsonDocument.Parse(result);
        var meta = document.RootElement.GetProperty("_meta");
        Assert.IsTrue(meta.GetProperty("producer").GetProperty("sentinel").GetBoolean());
        Assert.AreEqual(JsonValueKind.Object, meta.GetProperty("roslynMcp").ValueKind);
        Assert.AreEqual(0, reporter.ReportCount);
    }

    [TestMethod]
    [DataRow("{\"_meta\":\"producer-string\"}")]
    [DataRow("{\"_meta\":{\"roslynMcp\":{\"producer\":true}}}")]
    public void InjectMetaIfPossible_UnsafeCollision_PassesThroughAndReports(string original)
    {
        var reporter = new RecordingUnexpectedExceptionReporter();
        using var scope = AmbientGateMetrics.BeginRequest();

        var structured = JsonSerializer.SerializeToElement(new { producer = "structured-sentinel" });
        var input = new CallToolResult
        {
            Content = [new TextContentBlock { Text = original }],
            StructuredContent = structured,
        };

        var result = StructuredCallContentProjector.InjectMetaIntoContent(
            input,
            "sample_tool",
            reporter);

        Assert.AreSame(input, result);
        Assert.AreEqual(original, ((TextContentBlock)result.Content![0]).Text);
        Assert.AreEqual(structured.GetRawText(), result.StructuredContent!.Value.GetRawText());
        Assert.AreEqual(1, reporter.ReportCount);
        Assert.AreEqual(UnexpectedExceptionCategory.MetaProjection, reporter.Category);
    }

    private sealed class RecordingUnexpectedExceptionReporter : IUnexpectedExceptionReporter
    {
        public int ReportCount { get; private set; }
        public UnexpectedExceptionCategory? Category { get; private set; }
        public ServerUnexpectedExceptionDiagnostic? ServerDiagnostic { get; private set; }

        public UnexpectedExceptionDetails ReportUnexpected(
            Exception exception,
            UnexpectedExceptionCategory category)
        {
            ReportCount++;
            Category = category;
            var details = PublicExceptionDetailPolicy.ProjectUnexpected(exception, correlationId: null);
            ServerDiagnostic = details.Server;
            return details;
        }
    }
}
