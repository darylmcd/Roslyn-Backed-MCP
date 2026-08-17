using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

internal enum ServerObservabilitySinkKind
{
    Disabled,
    Stderr,
}

internal enum ServerObservabilityCategory
{
    ToolCall,
}

internal sealed record ServerObservabilityOptions(ServerObservabilitySinkKind Sink)
{
    public const string EnvironmentVariableName = "ROSLYNMCP_OBSERVABILITY_SINK";

    public static ServerObservabilityOptions Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "disabled" => new(ServerObservabilitySinkKind.Disabled),
            "stderr" => new(ServerObservabilitySinkKind.Stderr),
            _ => throw new ArgumentException(
                $"{EnvironmentVariableName} must be 'disabled' or 'stderr'.",
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

    ValueTask WriteAsync(ServerObservabilityEvent diagnosticEvent, CancellationToken cancellationToken);
}

internal sealed class DisabledServerObservabilitySink : IServerObservabilitySink
{
    public bool IsEnabled => false;

    public ValueTask WriteAsync(
        ServerObservabilityEvent diagnosticEvent,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class StderrServerObservabilitySink : IServerObservabilitySink
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly Func<string, CancellationToken, ValueTask> _write;

    public StderrServerObservabilitySink(
        Func<string, CancellationToken, ValueTask>? write = null)
    {
        _write = write ?? WriteToStderrAsync;
    }

    public bool IsEnabled => true;

    public ValueTask WriteAsync(
        ServerObservabilityEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(diagnosticEvent, s_jsonOptions);
        return _write(json, cancellationToken);
    }

    private static ValueTask WriteToStderrAsync(string json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Error.WriteLine(json);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Projects unexpected exceptions through the shared secret-safe policy and publishes the
/// server-only shape to the configured sink. Sink failures are isolated from MCP responses.
/// </summary>
internal sealed class ServerObservabilityReporter
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

    public async ValueTask<UnexpectedExceptionDetails> ReportUnexpectedAsync(
        Exception exception,
        ServerObservabilityCategory category,
        CancellationToken cancellationToken)
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
            await _sink.WriteAsync(
                new ServerObservabilityEvent(
                    DateTimeOffset.UtcNow,
                    LogLevel.Error,
                    categoryName,
                    UnexpectedFailureEventId,
                    UnexpectedFailureEventName,
                    details.Server),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
