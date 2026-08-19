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

    /// <summary>
    /// Era selector for the <c>resources/read</c> failure channel: revisions before
    /// 2026-07-28 report a missing resource with the dedicated <c>ResourceNotFound</c>
    /// (-32002) code, while 2026-07-28+ folds it into <c>InvalidParams</c> (-32602).
    /// The SDK's own <c>McpProtocolVersions</c> helper is <c>internal</c> and unavailable
    /// to server filters, so this named gate is the single local equivalent — filters must
    /// route through it rather than duplicating a protocol-string comparison.
    /// </summary>
    public static bool UseInvalidParamsForMissingResource<TParams>(RequestContext<TParams> context) =>
        SupportsJuly2026Features(context);
}
