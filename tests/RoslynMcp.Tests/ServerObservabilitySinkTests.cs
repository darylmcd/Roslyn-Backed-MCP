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
            JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, maxSegments: 2),
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

        // One record larger than the whole segment is replaced by a marker; it never rotates away
        // the earlier evidence and is never appended whole.
        logger.LogWarning("{Payload}", new string('x', 20_000));

        Assert.IsFalse(File.Exists(provider.SegmentPath(1)));
        var lines = File.ReadAllLines(provider.FilePath);
        Assert.HasCount(2, lines);
        Assert.AreEqual(firstLine, lines[0]);
        using var marker = JsonDocument.Parse(lines[1]);
        var dropped = marker.RootElement.GetProperty("fields").GetProperty("droppedBytes").GetInt64();
        Assert.IsGreaterThan(20_000, dropped);
        Assert.AreEqual(
            $"record truncated: {dropped} bytes exceeds segment limit",
            marker.RootElement.GetProperty("message").GetString());
    }

    [TestMethod]
    public void FileProvider_RotationKeepsFirstRecordReachableAcrossThreeThresholds()
    {
        var retention = JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, maxSegments: 3);
        using var provider = NewProvider(retention);
        var logger = provider.CreateLogger("evidence");
        logger.LogInformation(new EventId(1, "First"), "first-written-record");

        // Total volume = 3x the threshold; fixed record count so the loop cannot depend on what is retained.
        var beforeFiller = new FileInfo(provider.FilePath).Length;
        logger.LogInformation(new EventId(2, "Filler"), "filler {Index} {Pad}", 0, new string('p', 200));
        var fillerBytes = new FileInfo(provider.FilePath).Length - beforeFiller;
        for (var i = 1; i < 3 * retention.MaxSegmentBytes / fillerBytes; i++)
        {
            logger.LogInformation(new EventId(2, "Filler"), "filler {Index} {Pad}", i, new string('p', 200));
        }

        var retained = ReadRetainedLines(provider, retention.MaxSegments);
        Assert.IsTrue(retained.Any(line => line.Contains("first-written-record", StringComparison.Ordinal)),
            "The first-written record must stay reachable within the retained segment bound.");
        foreach (var line in retained)
        {
            using var parsedLine = JsonDocument.Parse(line);
        }
    }

    [TestMethod]
    public void FileProvider_RotationDeletesOnlyTheOldestSegmentAndRespectsCount()
    {
        var retention = JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, maxSegments: 2);
        using var provider = NewProvider(retention);
        var logger = provider.CreateLogger("count");
        for (var i = 0; i < 80; i++)
        {
            logger.LogInformation(new EventId(3, "Seq"), "seq-{Index} {Pad}", i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), new string('p', 400));
        }

        Assert.IsTrue(File.Exists(provider.SegmentPath(1)));
        Assert.IsTrue(File.Exists(provider.SegmentPath(2)));
        Assert.IsFalse(File.Exists(provider.SegmentPath(3)), "MaxSegments=2 must never create a third numbered segment.");
        var retained = ReadRetainedLines(provider, 2);
        var indexes = retained.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("fields")
            .GetProperty("index").GetString()!).ToArray();
        CollectionAssert.AreEqual(indexes.OrderBy(static value => value, StringComparer.Ordinal).ToArray(), indexes,
            "Retained segments read oldest to newest must be contiguous and ordered.");
        Assert.AreEqual("079", indexes[^1]);
        var oldestKept = int.Parse(indexes[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsGreaterThan(0, oldestKept, "Only the oldest segment may have been deleted.");
        for (var i = 0; i < indexes.Length; i++)
        {
            Assert.AreEqual(oldestKept + i, int.Parse(indexes[i], System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var path in new[] { provider.FilePath, provider.SegmentPath(1), provider.SegmentPath(2) })
        {
            Assert.IsLessThanOrEqualTo(retention.MaxSegmentBytes, new FileInfo(path).Length);
        }
    }

    [TestMethod]
    public void FileProvider_OversizedRecordBecomesBoundedValidMarkerInFirstEmptyAndNonEmptyFiles()
    {
        var retention = JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, maxSegments: 2);
        var hugeCategory = new string('c', 50_000);
        var hugeCorrelation = new string('k', 50_000);
        var timestamp = new DateTimeOffset(2026, 8, 26, 12, 34, 56, 789, TimeSpan.Zero);

        // First record ever (file absent), then an empty existing file, then a non-empty file.
        foreach (var setup in new[] { "absent", "empty", "nonempty" })
        {
            using var provider = new JsonLinesFileLoggerProvider(
                Path.Combine(TestTempRoot.Current, nameof(ServerObservabilitySinkTests), Guid.NewGuid().ToString("N")),
                retention, processId: 99, utcNow: () => timestamp);
            Directory.CreateDirectory(Path.GetDirectoryName(provider.FilePath)!);
            if (setup == "empty")
            {
                File.WriteAllText(provider.FilePath, string.Empty);
            }

            using var loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
            var logger = loggerFactory.CreateLogger(hugeCategory);
            if (setup == "nonempty")
            {
                logger.LogInformation("seed");
            }

            using (logger.BeginScope("correlationId={CorrelationId}", hugeCorrelation))
            {
                logger.LogError(new EventId(77, "BigEvent"), "{Payload}", new string('x', (int)retention.MaxSegmentBytes * 2));
            }

            var lines = File.ReadAllLines(provider.FilePath);
            Assert.HasCount(setup == "nonempty" ? 2 : 1, lines, setup);
            Assert.IsFalse(File.Exists(provider.SegmentPath(1)), setup + ": marker must not force a rotation.");
            var markerLine = lines[^1];
            Assert.IsLessThan(retention.MaxSegmentBytes, markerLine.Length * 2L, setup);
            using var document = JsonDocument.Parse(markerLine);
            var root = document.RootElement;
            Assert.AreEqual("2026-08-26T12:34:56.789Z", root.GetProperty("ts").GetString());
            Assert.AreEqual("Error", root.GetProperty("level").GetString());
            Assert.IsLessThanOrEqualTo(128, root.GetProperty("category").GetString()!.Length);
            Assert.IsTrue(hugeCategory.StartsWith(root.GetProperty("category").GetString()!, StringComparison.Ordinal));
            Assert.AreEqual(77, root.GetProperty("eventId").GetInt32());
            Assert.AreEqual("BigEvent", root.GetProperty("eventName").GetString());
            Assert.IsLessThanOrEqualTo(64, root.GetProperty("correlationId").GetString()!.Length);
            var fields = root.GetProperty("fields");
            Assert.AreEqual(retention.MaxSegmentBytes, fields.GetProperty("maxSegmentBytes").GetInt64());
            Assert.IsGreaterThan(retention.MaxSegmentBytes, fields.GetProperty("droppedBytes").GetInt64());
            Assert.IsTrue(root.GetProperty("message").GetString()!.StartsWith("record truncated: ", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void RetentionOptions_ParseDefaultsRejectInvalidValuesAndEnforceFloor()
    {
        var defaults = JsonLinesRetentionOptions.Parse(static _ => null);
        Assert.AreEqual(5L * 1024 * 1024, defaults.MaxSegmentBytes);
        Assert.AreEqual(3, defaults.MaxSegments);
        Assert.AreEqual(5L * 1024 * 1024, JsonLinesRetentionOptions.Parse(static _ => " ").MaxSegmentBytes);

        var parsed = JsonLinesRetentionOptions.Parse(name =>
            name == JsonLinesRetentionOptions.MaxSegmentBytesVariable ? " 16384 " : "7");
        Assert.AreEqual(16384L, parsed.MaxSegmentBytes);
        Assert.AreEqual(7, parsed.MaxSegments);

        var belowFloor = (JsonLinesRetentionOptions.MinimumSegmentBytes - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (variable, value) in new[]
        {
            (JsonLinesRetentionOptions.MaxSegmentBytesVariable, belowFloor),
            (JsonLinesRetentionOptions.MaxSegmentBytesVariable, "0"),
            (JsonLinesRetentionOptions.MaxSegmentBytesVariable, _secretSentinel),
            (JsonLinesRetentionOptions.MaxSegmentsVariable, "0"),
            (JsonLinesRetentionOptions.MaxSegmentsVariable, "-1"),
            (JsonLinesRetentionOptions.MaxSegmentsVariable, "101"),
            (JsonLinesRetentionOptions.MaxSegmentsVariable, _secretSentinel),
        })
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(
                () => JsonLinesRetentionOptions.Parse(name => name == variable ? value : null));
            Assert.Contains(variable, exception.Message);
            Assert.IsFalse(exception.Message.Contains(_secretSentinel, StringComparison.Ordinal));
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes - 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, 0));
    }

    [TestMethod]
    public void SegmentWriter_FailedRotationNeverLosesSegmentContentAcrossTheRetry()
    {
        var retention = JsonLinesRetentionOptions.Create(JsonLinesRetentionOptions.MinimumSegmentBytes, maxSegments: 3);
        var directory = Path.Combine(TestTempRoot.Current, nameof(ServerObservabilitySinkTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var writer = new JsonLinesSegmentWriter(directory, 4321, retention);
        File.WriteAllText(writer.ActivePath, "X");
        File.WriteAllText(writer.SegmentPath(1), "A");
        File.WriteAllText(writer.SegmentPath(3), "C");

        // Slot .2 is a non-empty directory: moving A onto it fails on every OS, with .3 holding live evidence.
        var blocker = writer.SegmentPath(2);
        Directory.CreateDirectory(blocker);
        File.WriteAllText(Path.Combine(blocker, "occupied"), "blocker");

        var failed = false;
        try
        {
            writer.Rotate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed = true;
        }

        Assert.IsTrue(failed, "The blocked shift must fail, as the sink's swallowed-failure path assumes.");
        Assert.AreEqual("C", File.ReadAllText(writer.SegmentPath(3)), "A failed rotation must not delete unmoved evidence.");

        Directory.Delete(blocker, recursive: true);
        writer.Rotate();

        // No segment is evicted (three segments fit), so X, A and C must all survive the failed + retried rotation.
        Assert.AreEqual("X", File.ReadAllText(writer.SegmentPath(1)));
        Assert.AreEqual("A", File.ReadAllText(writer.SegmentPath(2)));
        Assert.AreEqual("C", File.ReadAllText(writer.SegmentPath(3)));
        Assert.IsFalse(File.Exists(writer.ActivePath));
    }

    private static string[] ReadRetainedLines(JsonLinesFileLoggerProvider provider, int maxSegments) =>
        Enumerable.Range(1, maxSegments).Reverse().Select(provider.SegmentPath).Append(provider.FilePath)
            .Where(File.Exists).SelectMany(File.ReadAllLines).ToArray();

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

    private static JsonLinesFileLoggerProvider NewProvider(JsonLinesRetentionOptions? retention = null) => new(
        Path.Combine(TestTempRoot.Current, nameof(ServerObservabilitySinkTests), Guid.NewGuid().ToString("N")),
        retention);

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
