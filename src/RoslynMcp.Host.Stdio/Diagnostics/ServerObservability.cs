using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

internal enum ServerObservabilitySinkKind
{
    Disabled,
    Stderr,
    File,
}

internal sealed record ServerObservabilityOptions(ServerObservabilitySinkKind Sink)
{
    public const string EnvironmentVariableName = "ROSLYNMCP_OBSERVABILITY_SINK";

    public static ServerObservabilityOptions Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "disabled" => new(ServerObservabilitySinkKind.Disabled),
            "stderr" => new(ServerObservabilitySinkKind.Stderr),
            "file" => new(ServerObservabilitySinkKind.File),
            _ => throw new ArgumentException(
                $"{EnvironmentVariableName} must be 'disabled', 'stderr', or 'file'.",
                nameof(value)),
        };
}

internal sealed record ServerObservabilityEvent(
    DateTimeOffset TimestampUtc,
    LogLevel Level,
    string Category,
    int EventId,
    string EventName,
    ServerUnexpectedExceptionDiagnostic Exception);

internal interface IServerObservabilitySink
{
    bool IsEnabled { get; }

    void Write(ServerObservabilityEvent diagnosticEvent);
}

internal sealed class DisabledServerObservabilitySink : IServerObservabilitySink
{
    public bool IsEnabled => false;

    public void Write(ServerObservabilityEvent diagnosticEvent)
    {
    }
}

internal sealed class StderrServerObservabilitySink : IServerObservabilitySink
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly Action<string> _write;

    public StderrServerObservabilitySink(Action<string>? write = null)
    {
        _write = write ?? WriteToStderr;
    }

    public bool IsEnabled => true;

    public void Write(ServerObservabilityEvent diagnosticEvent)
    {
        var json = JsonSerializer.Serialize(diagnosticEvent, s_jsonOptions);
        _write(json);
    }

    private static void WriteToStderr(string json) => Console.Error.WriteLine(json);
}

internal sealed class FileServerObservabilitySink : IServerObservabilitySink
{
    private readonly ILogger _logger;

    public FileServerObservabilitySink(JsonLinesFileLoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _logger = provider.CreateLogger("RoslynMcp.ServerObservability");
    }

    public bool IsEnabled => true;

    public void Write(ServerObservabilityEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        _logger.Log(
            diagnosticEvent.Level,
            new EventId(diagnosticEvent.EventId, diagnosticEvent.EventName),
            "Unexpected {ExceptionCategory} failure; exceptionTypes={ExceptionTypes}; " +
            "stackFrameCount={StackFrameCount}",
            diagnosticEvent.Category,
            string.Join(',', diagnosticEvent.Exception.ExceptionTypes),
            diagnosticEvent.Exception.StackFrameCount);
    }
}

/// <summary>
/// Projects unexpected exceptions through the shared secret-safe policy and publishes the
/// server-only shape to the configured sink. Sink failures are isolated from MCP responses.
/// </summary>
internal sealed class ServerObservabilityReporter : IUnexpectedExceptionReporter
{
    private const int UnexpectedFailureEventId = 1001;
    private const string UnexpectedFailureEventName = "UnexpectedFailure";
    private readonly IServerObservabilitySink _sink;
    private readonly Action<string> _writeFallback;

    public ServerObservabilityReporter(IServerObservabilitySink sink)
        : this(sink, Console.Error.WriteLine)
    {
    }

    internal ServerObservabilityReporter(
        IServerObservabilitySink sink,
        Action<string> writeFallback)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(writeFallback);
        _sink = sink;
        _writeFallback = writeFallback;
    }

    public UnexpectedExceptionDetails ReportUnexpected(
        Exception exception,
        UnexpectedExceptionCategory category)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var categoryName = category.ToString();

        var details = PublicExceptionDetailPolicy.ProjectUnexpected(
            exception,
            RequestCorrelationContext.Current);
        if (!_sink.IsEnabled)
        {
            return details;
        }

        try
        {
            _sink.Write(
                new ServerObservabilityEvent(
                    DateTimeOffset.UtcNow,
                    LogLevel.Error,
                    categoryName,
                    UnexpectedFailureEventId,
                    UnexpectedFailureEventName,
                    details.Server));
        }
        catch
        {
            TryWriteFallback(details.Server.CorrelationId, categoryName);
        }

        return details;
    }

    private void TryWriteFallback(string correlationId, string category)
    {
        try
        {
            _writeFallback(
                "[roslyn-mcp] structured observability sink failed " +
                $"category={category} correlationId={correlationId}");
        }
        catch
        {
            // A closed stderr stream has no secondary safe destination. Never affect the response.
        }
    }
}
