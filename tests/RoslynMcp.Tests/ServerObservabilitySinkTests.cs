using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServerObservabilitySinkTests
{
    private const string _secretSentinel = "SECRET-SENTINEL-C:/private/source.cs";

    [TestMethod]
    public async Task Reporter_PreservesCorrelationAndSecretSafeStructureOnly()
    {
        var sink = new CapturingSink();
        var reporter = new ServerObservabilityReporter(sink);
        string correlationId;
        UnexpectedExceptionDetails details;
        string publicEnvelope;
        var exception = new SyntheticUnexpectedException(
            _secretSentinel,
            new IOException(_secretSentinel));

        using (RequestCorrelationContext.Begin())
        {
            correlationId = RequestCorrelationContext.Current!;
            details = await reporter.ReportUnexpectedAsync(
                exception,
                ServerObservabilityCategory.ToolCall,
                CancellationToken.None);
            publicEnvelope = ToolErrorHandler.ClassifyAndFormat(exception, "synthetic_tool");
        }

        Assert.AreEqual(correlationId, details.Public.CorrelationId);
        Assert.HasCount(1, sink.Events);
        var diagnosticEvent = sink.Events.Single();
        Assert.AreEqual(correlationId, diagnosticEvent.Exception.CorrelationId);
        Assert.AreEqual("ToolCall", diagnosticEvent.Category);
        Assert.AreEqual("UnexpectedFailure", diagnosticEvent.EventName);
        Assert.IsTrue(diagnosticEvent.Exception.ExceptionTypes.Any(
            static type => type.EndsWith(nameof(SyntheticUnexpectedException), StringComparison.Ordinal)));
        Assert.IsTrue(diagnosticEvent.Exception.ExceptionTypes.Any(
            static type => type.EndsWith(nameof(IOException), StringComparison.Ordinal)));

        using var publicDocument = JsonDocument.Parse(publicEnvelope);
        Assert.AreEqual(
            correlationId,
            publicDocument.RootElement.GetProperty("correlationId").GetString());
        Assert.IsFalse(publicEnvelope.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(publicDocument.RootElement.TryGetProperty("stackTrace", out _));

        var serialized = JsonSerializer.Serialize(diagnosticEvent);
        Assert.IsFalse(serialized.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("private/source.cs", StringComparison.Ordinal));
        Assert.IsNull(RequestCorrelationContext.Current);
    }

    [TestMethod]
    public async Task DisabledSink_ProducesNoOutputOrNetworkWork()
    {
        var sink = new DisabledServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);

        var details = await reporter.ReportUnexpectedAsync(
            new InvalidOperationException(_secretSentinel),
            ServerObservabilityCategory.ToolCall,
            CancellationToken.None);

        Assert.AreEqual("unavailable", details.Public.CorrelationId);
        Assert.IsFalse(sink.IsEnabled);
    }

    [TestMethod]
    public async Task StderrSink_EmitsCamelCaseStructuredJson()
    {
        var lines = new List<string>();
        var sink = new StderrServerObservabilitySink((line, _) =>
        {
            lines.Add(line);
            return ValueTask.CompletedTask;
        });
        var reporter = new ServerObservabilityReporter(sink);

        using (RequestCorrelationContext.Begin())
        {
            await reporter.ReportUnexpectedAsync(
                new InvalidOperationException(_secretSentinel),
                ServerObservabilityCategory.ToolCall,
                CancellationToken.None);
        }

        Assert.HasCount(1, lines);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.AreEqual("ToolCall", document.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("UnexpectedFailure", document.RootElement.GetProperty("eventName").GetString());
        Assert.IsFalse(lines[0].Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SinkFailure_FallsBackOnceWithoutChangingPublicDetails()
    {
        var fallbacks = new List<string>();
        var reporter = new ServerObservabilityReporter(
            new ThrowingSink(),
            fallbacks.Add);

        using (RequestCorrelationContext.Begin())
        {
            var details = await reporter.ReportUnexpectedAsync(
                new InvalidOperationException(_secretSentinel),
                ServerObservabilityCategory.ToolCall,
                CancellationToken.None);
            Assert.AreEqual(RequestCorrelationContext.Current, details.Public.CorrelationId);
        }

        Assert.HasCount(1, fallbacks);
        StringAssert.Contains(
            fallbacks[0],
            "structured observability sink failed",
            StringComparison.Ordinal);
        Assert.IsFalse(fallbacks[0].Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Options_DefaultDisabledAndRejectsUnknownValuesWithoutEchoingThem()
    {
        Assert.AreEqual(
            ServerObservabilitySinkKind.Disabled,
            ServerObservabilityOptions.Parse(null).Sink);
        Assert.AreEqual(
            ServerObservabilitySinkKind.Stderr,
            ServerObservabilityOptions.Parse(" stderr ").Sink);

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => ServerObservabilityOptions.Parse(_secretSentinel));
        Assert.IsFalse(exception.Message.Contains(_secretSentinel, StringComparison.Ordinal));
    }

    private sealed class CapturingSink : IServerObservabilitySink
    {
        public List<ServerObservabilityEvent> Events { get; } = [];
        public bool IsEnabled => true;

        public ValueTask WriteAsync(
            ServerObservabilityEvent diagnosticEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(diagnosticEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSink : IServerObservabilitySink
    {
        public bool IsEnabled => true;

        public ValueTask WriteAsync(
            ServerObservabilityEvent diagnosticEvent,
            CancellationToken cancellationToken) =>
            throw new IOException("sink unavailable");
    }

    private sealed class SyntheticUnexpectedException(string message, Exception innerException)
        : Exception(message, innerException);
}
