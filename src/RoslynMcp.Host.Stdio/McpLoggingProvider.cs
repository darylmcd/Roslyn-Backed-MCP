using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio;

/// <summary>
/// An ILoggerProvider that forwards .NET log messages to the MCP client via notifications/message.
/// Only active when an McpServer session is established.
/// Messages are sent as structured JSON objects with correlation IDs for observability.
/// </summary>
public sealed class McpLoggingProvider : ILoggerProvider
{
    private McpServer? _server;

    /// <summary>
    /// Attaches the active MCP server session so that log messages can be forwarded to the client.
    /// </summary>
    public void SetServer(McpServer server) => _server = server;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new McpLogger(this, categoryName);

    /// <inheritdoc/>
    public void Dispose() => _server = null;

    private sealed class McpLogger(McpLoggingProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider._server is not null && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var server = provider._server;
            if (server is null) return;

            var mcpLevel = logLevel switch
            {
                LogLevel.Trace => LoggingLevel.Debug,
                LogLevel.Debug => LoggingLevel.Debug,
                LogLevel.Information => LoggingLevel.Info,
                LogLevel.Warning => LoggingLevel.Warning,
                LogLevel.Error => LoggingLevel.Error,
                LogLevel.Critical => LoggingLevel.Critical,
                _ => LoggingLevel.Info
            };

            var message = formatter(state, exception);

            // Build structured log entry with correlation ID for observability
            var structuredEntry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                level = mcpLevel.ToString(),
                logger = categoryName,
                message,
                correlationId = CorrelationContext.Current,
                eventId = eventId.Id != 0 ? eventId.Id : (int?)null,
                eventName = eventId.Name,
                exception = exception?.ToString()
            };

            // Fire-and-forget: logging should not block the caller
            _ = SendLogAsync(server, mcpLevel, categoryName, structuredEntry);
        }

        private static async Task SendLogAsync(McpServer server, LoggingLevel level, string logger, object structuredData)
        {
            try
            {
                await server.SendNotificationAsync(
                    NotificationMethods.LoggingMessageNotification,
                    new LoggingMessageNotificationParams
                    {
                        Level = level,
                        Logger = logger,
                        Data = JsonSerializer.SerializeToElement(structuredData)
                    }).ConfigureAwait(false);
            }
            catch
            {
                // Swallow: logging should never throw
            }
        }
    }
}

/// <summary>
/// Ambient correlation ID context for structured logging. Each tool invocation gets a unique ID
/// to correlate log entries across the request lifecycle.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _correlationId = new();

    /// <summary>Gets the current correlation ID, or generates one if none is set.</summary>
    public static string Current => _correlationId.Value ??= GenerateId();

    /// <summary>Sets a new correlation ID for the current async context.</summary>
    public static IDisposable BeginScope()
    {
        var previous = _correlationId.Value;
        _correlationId.Value = GenerateId();
        return new CorrelationScope(previous);
    }

    private static string GenerateId() => Guid.NewGuid().ToString("N")[..12];

    private sealed class CorrelationScope(string? previous) : IDisposable
    {
        public void Dispose() => _correlationId.Value = previous;
    }
}
