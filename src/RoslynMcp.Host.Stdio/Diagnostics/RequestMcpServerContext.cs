using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Exposes the current request's MCP server to lower host-layer dispatch helpers that must
/// preserve connection-scoped policy, such as the legacy client-roots narrowing boundary.
/// </summary>
internal static class RequestMcpServerContext
{
    private static readonly AsyncLocal<McpServer?> s_current = new();

    public static McpServer? Current => s_current.Value;

    public static IDisposable Begin(McpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var prior = s_current.Value;
        s_current.Value = server;
        return new Scope(prior, server);
    }

    private sealed class Scope(McpServer? prior, McpServer current) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(s_current.Value, current))
            {
                s_current.Value = prior;
            }
        }
    }
}
