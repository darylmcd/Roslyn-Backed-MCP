using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
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

    /// <summary>
    /// An unset, empty or whitespace value selects the bounded file sink: stderr is usually swallowed by the
    /// MCP host, so without a default file sink the first failure leaves no record an agent can reach.
    /// <c>disabled</c> is the explicit opt-out. See ADR 0042.
    /// </summary>
    public static ServerObservabilityOptions Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "file" => new(ServerObservabilitySinkKind.File),
            "disabled" => new(ServerObservabilitySinkKind.Disabled),
            "stderr" => new(ServerObservabilitySinkKind.Stderr),
            _ => throw new InvalidOperationException(
                $"{EnvironmentVariableName} must be 'disabled', 'stderr', or 'file' (unset selects 'file')."),
        };
}

/// <summary>
/// Projects the resolved sink into the one status shape both the startup record and
/// <c>server_info.observability</c> report, so the log line and the tool response cannot drift apart.
/// </summary>
internal static class ServerObservabilityStatus
{
    private static readonly Action<ILogger, string, string?, string?, Exception?> LogConfigured =
        LoggerMessage.Define<string, string?, string?>(
            LogLevel.Information,
            new EventId(LogEvents.ObservabilitySinkConfigured, LogEvents.ObservabilitySinkConfiguredName),
            "observability sink configured: sink={Sink} logFile={LogFile} logDirectory={LogDirectory}");

    /// <summary>
    /// <paramref name="fileProvider"/> is the file provider Program.cs installed; it is non-null exactly when
    /// the sink is <c>file</c>, and is the only source of the active-file path.
    /// </summary>
    public static ObservabilityDto Describe(
        ServerObservabilityOptions options,
        JsonLinesFileLoggerProvider? fileProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sink = options.Sink switch
        {
            ServerObservabilitySinkKind.File => "file",
            ServerObservabilitySinkKind.Stderr => "stderr",
            ServerObservabilitySinkKind.Disabled => "disabled",
            _ => throw new InvalidOperationException("Unsupported server observability sink."),
        };
        return fileProvider is null
            ? new ObservabilityDto(sink, LogFile: null, LogDirectory: null)
            : new ObservabilityDto(sink, fileProvider.FilePath, Path.GetDirectoryName(fileProvider.FilePath));
    }

    public static void Log(ILogger logger, ObservabilityDto status)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(status);
        LogConfigured(logger, status.Sink, status.LogFile, status.LogDirectory, null);
    }
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
            "stackFrameCount={StackFrameCount}; correlationId={CorrelationId}",
            diagnosticEvent.Category,
            diagnosticEvent.Exception.ExceptionTypes.ToArray(),
            diagnosticEvent.Exception.StackFrameCount,
            diagnosticEvent.Exception.CorrelationId);
    }
}

/// <summary>
/// Projects unexpected exceptions through the shared secret-safe policy and publishes the
/// server-only shape to the configured sink. Sink failures are isolated from MCP responses.
/// </summary>
internal sealed class ServerObservabilityReporter : IUnexpectedExceptionReporter
{
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
                    LogEvents.UnexpectedFailure,
                    LogEvents.UnexpectedFailureName,
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
