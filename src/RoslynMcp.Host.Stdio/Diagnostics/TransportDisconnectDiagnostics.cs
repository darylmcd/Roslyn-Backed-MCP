using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

internal static class TransportDisconnectDiagnostics
{
    private static readonly Action<ILogger, string, string, Exception?> LogTransportDisconnected =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(LogEvents.TransportDisconnected, LogEvents.TransportDisconnectedName),
            "category=transport-disconnected exceptionType={ExceptionType} correlationId={CorrelationId}");

    public static string Log(ILogger logger, Exception exception)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        LogTransportDisconnected(logger, exception.GetType().Name, correlationId, null);
        return correlationId;
    }
}
