using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.ProtocolCompatibility;

/// <summary>
/// Resolves protocol-era and client-capability state for the current request. This neutral host
/// boundary is shared by result shaping and request-scoped input; neither layer owns it.
/// </summary>
internal static class RequestProtocolFeatureGate
{
    internal const string July2026ProtocolVersion = "2026-07-28";

    public static bool SupportsJuly2026Features<TParams>(RequestContext<TParams> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var protocolVersion = context.JsonRpcRequest.Context?.ProtocolVersion
            ?? context.Server.NegotiatedProtocolVersion;
        // MCP revision identifiers are ISO-8601 dates. Mirror the SDK's gate exactly; its
        // McpProtocolVersions helper is internal and therefore unavailable to server filters.
        return !string.IsNullOrEmpty(protocolVersion)
            && StringComparer.Ordinal.Compare(protocolVersion, July2026ProtocolVersion) >= 0;
    }

    /// <summary>
    /// Resolves the authoritative client capabilities for this request. SEP-2575 revisions carry
    /// capabilities in request metadata and prohibit inference from server/session state; older
    /// initialize-handshake revisions expose the session-scoped snapshot on the server.
    /// </summary>
    public static ClientCapabilities? ResolveClientCapabilities<TParams>(RequestContext<TParams> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return SupportsJuly2026Features(context)
            ? context.JsonRpcRequest.Context?.ClientCapabilities
            : context.Server.ClientCapabilities;
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
