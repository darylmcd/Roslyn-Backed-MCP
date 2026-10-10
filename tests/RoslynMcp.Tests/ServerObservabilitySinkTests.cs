using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ServerObservabilitySinkTests
{
    private const string _secretSentinel = "SECRET-SENTINEL-C:/private/source.cs";

    [TestMethod]
    public void Reporter_PreservesCorrelationAndSecretSafeStructureOnly()
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
            details = reporter.ReportUnexpected(
                exception,
                UnexpectedExceptionCategory.ToolCall);
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
    public void DisabledSink_ProducesNoOutputOrNetworkWork()
    {
        var sink = new DisabledServerObservabilitySink();
        var reporter = new ServerObservabilityReporter(sink);

        var details = reporter.ReportUnexpected(
            new InvalidOperationException(_secretSentinel),
            UnexpectedExceptionCategory.ToolCall);

        Assert.AreEqual("unavailable", details.Public.CorrelationId);
        Assert.IsFalse(sink.IsEnabled);
    }

    [TestMethod]
    public void StderrSink_EmitsCamelCaseStructuredJson()
    {
        var lines = new List<string>();
        var sink = new StderrServerObservabilitySink(line =>
        {
            lines.Add(line);
        });
        var reporter = new ServerObservabilityReporter(sink);

        using (RequestCorrelationContext.Begin())
        {
            reporter.ReportUnexpected(
                new InvalidOperationException(_secretSentinel),
                UnexpectedExceptionCategory.ToolCall);
        }

        Assert.HasCount(1, lines);
        using var document = JsonDocument.Parse(lines[0]);
        Assert.AreEqual("ToolCall", document.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("UnexpectedFailure", document.RootElement.GetProperty("eventName").GetString());
        Assert.IsFalse(lines[0].Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void SinkFailure_FallsBackOnceWithoutChangingPublicDetails()
    {
        var fallbacks = new List<string>();
        var reporter = new ServerObservabilityReporter(
            new ThrowingSink(),
            fallbacks.Add);

        using (RequestCorrelationContext.Begin())
        {
            var details = reporter.ReportUnexpected(
                new InvalidOperationException(_secretSentinel),
                UnexpectedExceptionCategory.ToolCall);
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
        Assert.AreEqual(
            ServerObservabilitySinkKind.File,
            ServerObservabilityOptions.Parse(" file ").Sink);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => ServerObservabilityOptions.Parse(_secretSentinel));
        Assert.IsFalse(exception.Message.Contains(_secretSentinel, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FileProvider_WritesScopedJsonLinesAndRotatesWithoutExceptionDetail()
    {
        var logDirectory = Path.Combine(
            TestTempRoot.Current,
            nameof(ServerObservabilitySinkTests),
            Guid.NewGuid().ToString("N"));
        var timestamp = new DateTimeOffset(2026, 8, 26, 12, 34, 56, 789, TimeSpan.Zero);
        using var provider = new JsonLinesFileLoggerProvider(
            logDirectory,
            maxFileBytes: 450,
            processId: 1234,
            utcNow: () => timestamp);
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        });
        var logger = loggerFactory.CreateLogger("Synthetic.Category");

        using (logger.BeginScope("correlationId={CorrelationId}", "correlation-123"))
        {
            logger.LogError(
                new EventId(42, "SyntheticEvent"),
                new InvalidOperationException(_secretSentinel),
                "Safe message {Value}",
                7);
        }

        var firstLine = File.ReadLines(provider.FilePath).Single();
        using (var document = JsonDocument.Parse(firstLine))
        {
            var root = document.RootElement;
            Assert.AreEqual("2026-08-26T12:34:56.789Z", root.GetProperty("ts").GetString());
            Assert.AreEqual("Error", root.GetProperty("level").GetString());
            Assert.AreEqual("Synthetic.Category", root.GetProperty("category").GetString());
            Assert.AreEqual(42, root.GetProperty("eventId").GetInt32());
            Assert.AreEqual("Safe message 7", root.GetProperty("message").GetString());
            Assert.AreEqual("correlation-123", root.GetProperty("correlationId").GetString());
        }

        Assert.IsFalse(firstLine.Contains(_secretSentinel, StringComparison.Ordinal));

        logger.LogWarning("{Payload}", new string('x', 500));

        Assert.IsTrue(File.Exists(provider.RotatedFilePath));
        Assert.AreEqual(firstLine, File.ReadLines(provider.RotatedFilePath).Single());
        Assert.HasCount(1, File.ReadLines(provider.FilePath).ToArray());
    }

    [TestMethod]
    public void FileProvider_PreservesTypedFieldsAndSafeProjection()
    {
        using var provider = NewProvider();
        var logger = provider.CreateLogger("typed");
        logger.LogInformation(new EventId(10, "Typed"), "{ToolName} {ElapsedMs} {Missing} {Types} {Opaque}",
            "compile_check", 12.5, null, new[] { "IOException", "ArgumentException" }, new SafeOpaque());
        LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(11), "{ToolName} {ElapsedMs}")(logger, "defined", 8, null);
        var lines = File.ReadAllLines(provider.FilePath);
        using var first = JsonDocument.Parse(lines[0]);
        var root = first.RootElement;
        Assert.HasCount(8, root.EnumerateObject().ToArray());
        Assert.AreEqual("Typed", root.GetProperty("eventName").GetString());
        var fields = root.GetProperty("fields");
        Assert.AreEqual("compile_check", fields.GetProperty("toolName").GetString());
        Assert.AreEqual(12.5, fields.GetProperty("elapsedMs").GetDouble());
        Assert.AreEqual(JsonValueKind.Null, fields.GetProperty("missing").ValueKind);
        Assert.AreEqual(2, fields.GetProperty("types").GetArrayLength());
        Assert.AreEqual(typeof(SafeOpaque).FullName, fields.GetProperty("opaque").GetString());
        Assert.HasCount(5, fields.EnumerateObject().ToArray());
        Assert.IsFalse(lines[0].Contains(_secretSentinel, StringComparison.Ordinal));
        using var second = JsonDocument.Parse(lines[1]);
        Assert.AreEqual(JsonValueKind.Null, second.RootElement.GetProperty("eventName").ValueKind);
        Assert.AreEqual(8, second.RootElement.GetProperty("fields").GetProperty("elapsedMs").GetInt32());
    }

    [TestMethod]
    public void FileProvider_PreservesRepeatedAndCollidingNames()
    {
        using var provider = NewProvider();
        provider.CreateLogger("collisions").LogInformation("{Name} {name} {Name} {name_2}", 1, 2, 3, 4);
        using var document = JsonDocument.Parse(File.ReadAllText(provider.FilePath));
        var fields = document.RootElement.GetProperty("fields");
        Assert.HasCount(4, fields.EnumerateObject().ToArray());
        Assert.AreEqual(1, fields.GetProperty("name").GetInt32());
        Assert.AreEqual(2, fields.GetProperty("name_2").GetInt32());
        Assert.AreEqual(3, fields.GetProperty("name_3").GetInt32());
        Assert.AreEqual(4, fields.GetProperty("name_2_2").GetInt32());
    }

    [TestMethod]
    public void FileProvider_DoesNotInspectCustomStateOrPromoteCustomFormatterValues()
    {
        using var provider = NewProvider();
        var logger = provider.CreateLogger("privacy");
        logger.Log(LogLevel.Information, new EventId(1), new ThrowingState(), null, static (_, _) => "safe-custom");
        new TemplateCaptureLogger(logger).LogInformation("{Secret}", _secretSentinel);
        var lines = File.ReadAllLines(provider.FilePath);
        Assert.HasCount(2, lines);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.HasCount(0, document.RootElement.GetProperty("fields").EnumerateObject().ToArray());
            Assert.IsFalse(line.Contains(_secretSentinel, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void FileProvider_IsolatesFormatterAndProjectionAndIgnoresOpaqueScopes()
    {
        using var provider = NewProvider();
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("isolation");
        logger.Log(LogLevel.Warning, new EventId(1), "state", null,
            static (_, _) => throw new InvalidOperationException("formatter-failed"));
        logger.LogInformation("{Value}", new ThrowingOpaque());
        using (logger.BeginScope(new ThrowingState()))
        {
            logger.LogInformation("safe-scope");
        }
        logger.LogInformation("survived");
        Assert.HasCount(2, File.ReadAllLines(provider.FilePath));
    }

    [TestMethod]
    public void FileSink_ProjectsExceptionArraysAndCorrelationWithoutSecretDetail()
    {
        using var provider = NewProvider();
        var reporter = new ServerObservabilityReporter(new FileServerObservabilitySink(provider));
        string correlation;
        using (RequestCorrelationContext.Begin())
        {
            correlation = RequestCorrelationContext.Current!;
            reporter.ReportUnexpected(new InvalidOperationException(_secretSentinel, new IOException(_secretSentinel)), UnexpectedExceptionCategory.ToolCall);
        }
        var line = File.ReadAllText(provider.FilePath);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.AreEqual(correlation, root.GetProperty("correlationId").GetString());
        var fields = root.GetProperty("fields");
        Assert.AreEqual(2, fields.GetProperty("exceptionTypes").GetArrayLength());
        Assert.IsTrue(fields.GetProperty("stackFrameCount").GetInt32() >= 0);
        Assert.IsFalse(line.Contains(_secretSentinel, StringComparison.Ordinal));
        Assert.IsFalse(line.Contains("stackTrace", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FileProvider_ExceptionFieldsExposeTypeOnly()
    {
        using var provider = NewProvider();
        provider.CreateLogger("exception").LogInformation("{Failure}", new SafeTextException());
        using var document = JsonDocument.Parse(File.ReadAllText(provider.FilePath));
        Assert.AreEqual(typeof(SafeTextException).FullName,
            document.RootElement.GetProperty("fields").GetProperty("failure").GetString());
    }

    [TestMethod]
    public void FileProvider_DoesNotInvokeOpaqueFormattingTwice()
    {
        using var provider = NewProvider();
        var value = new StatefulOpaque();
        provider.CreateLogger("opaque").LogInformation("{Value}", value);
        var line = File.ReadAllText(provider.FilePath);
        using var document = JsonDocument.Parse(line);
        Assert.AreEqual("safe-first", document.RootElement.GetProperty("message").GetString());
        Assert.AreEqual(typeof(StatefulOpaque).FullName, document.RootElement.GetProperty("fields").GetProperty("value").GetString());
        Assert.AreEqual(1, value.Calls);
    }

    private sealed class StatefulOpaque
    {
        public int Calls { get; private set; }
        public override string ToString() => ++Calls == 1 ? "safe-first" : throw new InvalidOperationException("second-call-must-not-run");
    }

    [TestMethod]
    public void FileProvider_PreservesPrimitiveKindsAndCorrelationPrecedence()
    {
        using var provider = NewProvider();
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("primitives");
        using (logger.BeginScope("{CorrelationId}", "scope-id"))
        {
            logger.LogInformation("{CorrelationId} {Boolean} {Byte} {SByte} {Short} {UShort} {Int} {UInt} {Long} {ULong} {Float} {Double} {Decimal} {Char}",
                "state-id", true, (byte)1, (sbyte)2, (short)3, (ushort)4, 5, 6u, 7L, 8UL, 9f, 10d, 11m, 'x');
        }
        using var document = JsonDocument.Parse(File.ReadAllText(provider.FilePath));
        var root = document.RootElement;
        Assert.AreEqual("state-id", root.GetProperty("correlationId").GetString());
        var fields = root.GetProperty("fields");
        Assert.IsTrue(fields.GetProperty("boolean").GetBoolean());
        foreach (var key in new[] { "byte", "sByte", "short", "uShort", "int", "uInt", "long", "uLong", "float", "double", "decimal" })
        {
            Assert.AreEqual(JsonValueKind.Number, fields.GetProperty(key).ValueKind, key);
        }
        Assert.AreEqual("x", fields.GetProperty("char").GetString());
    }

    [TestMethod]
    [DoNotParallelize]
    public void FileProvider_ReportsFailureOnceAndStopsFormattingAfterDisposal()
    {
        using var provider = NewProvider();
        var logger = provider.CreateLogger("failures");
        var originalError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            logger.LogInformation("{Value}", double.NaN);
            logger.Log(LogLevel.Warning, new EventId(1), "state", null,
                static (_, _) => throw new InvalidOperationException(_secretSentinel));
            provider.Dispose();
            logger.Log(LogLevel.Warning, new EventId(1), "state", null,
                static (_, _) => throw new OutOfMemoryException("must-not-format-after-disposal"));
        }
        finally
        {
            Console.SetError(originalError);
        }
        Assert.AreEqual("[roslyn-mcp] JSON-lines observability file sink failed." + Environment.NewLine, error.ToString());
        Assert.IsFalse(File.Exists(provider.FilePath));
    }

    [TestMethod]
    public void FileProvider_DoesNotSwallowOutOfMemory()
    {
        using var provider = NewProvider();
        Assert.ThrowsExactly<OutOfMemoryException>(() =>
            provider.CreateLogger("oom").Log(LogLevel.Error, new EventId(1), "state", null,
                static (_, _) => throw new OutOfMemoryException()));
    }

    [TestMethod]
    public void FileProvider_CorrelationUsesSafeScalarsAndIgnoresOpaqueStateAndScopes()
    {
        using var provider = NewProvider();
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("correlation");
        var stateValue = new StatefulOpaque();
        logger.LogInformation("{CorrelationId}", stateValue);
        var scopeValue = new StatefulOpaque();
        using (logger.BeginScope("{CorrelationId}", scopeValue))
        {
            logger.LogInformation("safe-scope");
        }
        logger.LogInformation("{CorrelationId}", 42);
        using (logger.BeginScope("{CorrelationId}", 73))
        {
            logger.LogInformation("numeric-scope");
        }
        logger.LogInformation("{CorrelationId}", "string-id");
        using (logger.BeginScope("{CorrelationId}", "fallback-id"))
        {
            logger.LogInformation("{CorrelationId}", (object?)null);
        }
        logger.LogInformation("{CorrelationId}", 'c');
        logger.LogInformation("{CorrelationId}", (object)new[] { "first", "second" });
        var records = File.ReadAllLines(provider.FilePath).Select(static line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.HasCount(8, records);
            Assert.AreEqual(JsonValueKind.Null, records[0].RootElement.GetProperty("correlationId").ValueKind);
            Assert.AreEqual("safe-first", records[0].RootElement.GetProperty("message").GetString());
            Assert.AreEqual(JsonValueKind.Null, records[1].RootElement.GetProperty("correlationId").ValueKind);
            Assert.AreEqual("42", records[2].RootElement.GetProperty("correlationId").GetString());
            Assert.AreEqual("73", records[3].RootElement.GetProperty("correlationId").GetString());
            Assert.AreEqual("string-id", records[4].RootElement.GetProperty("correlationId").GetString());
            Assert.AreEqual("fallback-id", records[5].RootElement.GetProperty("correlationId").GetString());
            Assert.AreEqual("c", records[6].RootElement.GetProperty("correlationId").GetString());
            Assert.AreEqual(JsonValueKind.Null, records[7].RootElement.GetProperty("correlationId").ValueKind);
            Assert.AreEqual(2, records[7].RootElement.GetProperty("fields").GetProperty("correlationId").GetArrayLength());
            Assert.AreEqual(1, stateValue.Calls);
            Assert.AreEqual(0, scopeValue.Calls);
        }
        finally
        {
            foreach (var record in records)
            {
                record.Dispose();
            }
        }
    }

    private static JsonLinesFileLoggerProvider NewProvider() => new(
        Path.Combine(TestTempRoot.Current, nameof(ServerObservabilitySinkTests), Guid.NewGuid().ToString("N")));

    private sealed class SafeOpaque
    {
        public string Secret => _secretSentinel;
        public string Throwing => throw new InvalidOperationException("getter-must-not-run");
        public override string ToString() => "safe-opaque";
    }

    private sealed class ThrowingOpaque
    {
        public override string ToString() => throw new InvalidOperationException("projection-failed");
    }

    private sealed class SafeTextException() : Exception(_secretSentinel)
    {
        public override string ToString() => "safe-exception";
    }

    private sealed class ThrowingState : IEnumerable<KeyValuePair<string, object?>>
    {
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => throw new InvalidOperationException("enumeration-must-not-run");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TemplateCaptureLogger(ILogger destination) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            destination.Log(level, eventId, state, exception, static (_, _) => "omitted");
    }

    private sealed class CapturingSink : IServerObservabilitySink
    {
        public List<ServerObservabilityEvent> Events { get; } = [];
        public bool IsEnabled => true;

        public void Write(ServerObservabilityEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }

    private sealed class ThrowingSink : IServerObservabilitySink
    {
        public bool IsEnabled => true;

        public void Write(ServerObservabilityEvent diagnosticEvent) =>
            throw new IOException("sink unavailable");
    }

    private sealed class SyntheticUnexpectedException(string message, Exception innerException)
        : Exception(message, innerException);
}
