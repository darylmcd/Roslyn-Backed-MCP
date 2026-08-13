using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Applies conservative caching hints to resource bodies. Resource reads may reflect live
/// workspace state or the process's selected surface profile, so clients must treat every body
/// as immediately stale and must never share it across users or server processes.
/// </summary>
internal static class ResourceReadResultFilter
{
    internal static readonly TimeSpan CacheTimeToLive = TimeSpan.Zero;

    public static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> Create(
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next) =>
        async (context, cancellationToken) =>
            Normalize(await next(context, cancellationToken).ConfigureAwait(false));

    internal static ReadResourceResult Normalize(ReadResourceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.TimeToLive = CacheTimeToLive;
        result.CacheScope = CacheScope.Private;
        return result;
    }
}
