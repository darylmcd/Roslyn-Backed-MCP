using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Makes static MCP discovery surfaces deterministic and adds caching hints on protocol revisions
/// that define them. Private scope prevents a shared gateway from reusing one process's
/// tier-filtered surface for another user or server.
/// </summary>
internal static class StaticListResultFilter
{
    internal static readonly TimeSpan CacheTimeToLive = TimeSpan.FromMinutes(5);

    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> CreateTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
            Normalize(
                await next(context, cancellationToken).ConfigureAwait(false),
                RequestProtocolFeatureGate.SupportsJuly2026Features(context));

    public static McpRequestHandler<ListPromptsRequestParams, ListPromptsResult> CreatePrompts(
        McpRequestHandler<ListPromptsRequestParams, ListPromptsResult> next) =>
        async (context, cancellationToken) =>
            Normalize(
                await next(context, cancellationToken).ConfigureAwait(false),
                RequestProtocolFeatureGate.SupportsJuly2026Features(context));

    public static McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> CreateResources(
        McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next) =>
        async (context, cancellationToken) =>
            Normalize(
                await next(context, cancellationToken).ConfigureAwait(false),
                RequestProtocolFeatureGate.SupportsJuly2026Features(context));

    public static McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> CreateResourceTemplates(
        McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> next) =>
        async (context, cancellationToken) =>
            Normalize(
                await next(context, cancellationToken).ConfigureAwait(false),
                RequestProtocolFeatureGate.SupportsJuly2026Features(context));

    internal static ListToolsResult Normalize(ListToolsResult result, bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Tools = (result.Tools ?? []).OrderBy(static tool => tool.Name, StringComparer.Ordinal).ToArray();
        SetCachingHints(result, supportsCachingHints);
        return result;
    }

    internal static ListPromptsResult Normalize(ListPromptsResult result, bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Prompts = (result.Prompts ?? []).OrderBy(static prompt => prompt.Name, StringComparer.Ordinal).ToArray();
        SetCachingHints(result, supportsCachingHints);
        return result;
    }

    internal static ListResourcesResult Normalize(ListResourcesResult result, bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Resources = (result.Resources ?? []).OrderBy(static resource => resource.Uri, StringComparer.Ordinal).ToArray();
        SetCachingHints(result, supportsCachingHints);
        return result;
    }

    internal static ListResourceTemplatesResult Normalize(
        ListResourceTemplatesResult result,
        bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.ResourceTemplates = (result.ResourceTemplates ?? [])
            .OrderBy(static template => template.UriTemplate, StringComparer.Ordinal)
            .ToArray();
        SetCachingHints(result, supportsCachingHints);
        return result;
    }

    private static void SetCachingHints(ICacheableResult result, bool supportsCachingHints)
    {
        result.TimeToLive = supportsCachingHints ? CacheTimeToLive : null;
        result.CacheScope = supportsCachingHints ? CacheScope.Private : null;
    }
}
