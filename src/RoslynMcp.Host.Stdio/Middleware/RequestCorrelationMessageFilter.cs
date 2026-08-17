using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>Creates and clears one correlation scope around every incoming MCP message.</summary>
internal static class RequestCorrelationMessageFilter
{
    public static McpMessageHandler Create(McpMessageHandler next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async (context, cancellationToken) =>
        {
            using var scope = RequestCorrelationContext.Begin();
            await next(context, cancellationToken).ConfigureAwait(false);
        };
    }
}
