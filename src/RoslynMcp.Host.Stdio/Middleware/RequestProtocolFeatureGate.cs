using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Resolves protocol feature support for the current request. Per-request metadata takes
/// precedence so stateless transports do not accidentally inherit connection-scoped behavior.
/// </summary>
internal static class RequestProtocolFeatureGate
{
    internal const string July2026ProtocolVersion = "2026-07-28";

    public static bool SupportsJuly2026Features<TParams>(RequestContext<TParams> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var protocolVersion = context.JsonRpcRequest.Context?.ProtocolVersion
            ?? context.Server.NegotiatedProtocolVersion;
        // MCP revision identifiers are ISO-8601 dates. Mirror the SDK 2.1 gate exactly; its
        // McpProtocolVersions helper is internal and therefore unavailable to server filters.
        return !string.IsNullOrEmpty(protocolVersion)
            && StringComparer.Ordinal.Compare(protocolVersion, July2026ProtocolVersion) >= 0;
    }
}
