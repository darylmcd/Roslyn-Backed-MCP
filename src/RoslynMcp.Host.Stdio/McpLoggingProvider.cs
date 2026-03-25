using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio;

/// <summary>
/// An ILoggerProvider that forwards .NET log messages to the MCP client via notifications/message.
/// Only active when an McpServer session is established.
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
            if (exception is not null)
                message += $"\n{exception}";

            // Fire-and-forget: logging should not block the caller
            _ = SendLogAsync(server, mcpLevel, categoryName, message);
        }

        private static async Task SendLogAsync(McpServer server, LoggingLevel level, string logger, string message)
        {
            try
            {
                await server.SendNotificationAsync(
                    NotificationMethods.LoggingMessageNotification,
                    new LoggingMessageNotificationParams
                    {
                        Level = level,
                        Logger = logger,
                        Data = JsonSerializer.SerializeToElement(message)
                    }).ConfigureAwait(false);
            }
            catch
            {
                // Swallow: logging should never throw
            }
        }
    }
}
