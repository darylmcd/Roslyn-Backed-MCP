using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Applies conservative caching hints to resource bodies on protocol revisions that define them.
/// Resource reads may reflect live workspace state or the process's selected surface profile, so
/// clients must treat every body as immediately stale and never share it across users or servers.
/// </summary>
internal static class ResourceReadResultFilter
{
    internal static readonly TimeSpan CacheTimeToLive = TimeSpan.Zero;

    public static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> Create(
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next) =>
        async (context, cancellationToken) =>
            Normalize(
                await next(context, cancellationToken).ConfigureAwait(false),
                RequestProtocolFeatureGate.SupportsJuly2026Features(context));

    internal static ReadResourceResult Normalize(ReadResourceResult result, bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.TimeToLive = supportsCachingHints ? CacheTimeToLive : null;
        result.CacheScope = supportsCachingHints ? CacheScope.Private : null;
        return result;
    }
}
