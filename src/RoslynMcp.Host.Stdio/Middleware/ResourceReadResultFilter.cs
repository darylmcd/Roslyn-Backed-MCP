using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Read boundary for <c>resources/read</c>. On success, applies conservative caching hints to
/// resource bodies on protocol revisions that define them — resource reads may reflect live
/// workspace state or the process's selected surface profile, so clients must treat every body
/// as immediately stale and never share it across users or servers. On failure, translates the
/// handler exception into a JSON-RPC protocol error (<see cref="McpProtocolException"/>) so a
/// failed read answers on the error channel instead of a "successful" result whose contents
/// body is a serialized error document. Error payloads carry only the stable category, its
/// remediation text, the offending parameter name, the requested URI, and a correlation id —
/// never the exception type, a stack frame, or a raw server-side path.
/// </summary>
internal static class ResourceReadResultFilter
{
    internal static readonly TimeSpan CacheTimeToLive = TimeSpan.Zero;

    public static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> Create(
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return Normalize(
                    await next(context, cancellationToken).ConfigureAwait(false),
                    RequestProtocolFeatureGate.SupportsJuly2026Features(context));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (McpException)
            {
                // Already protocol-shaped — e.g. the SDK's unknown-resource rejection, or a
                // McpProtocolException with an explicit error code. Never reclassify.
                throw;
            }
            catch (Exception ex)
            {
                throw TranslateToProtocolError(ex, context);
            }
        };

    internal static ReadResourceResult Normalize(ReadResourceResult result, bool supportsCachingHints)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.TimeToLive = supportsCachingHints ? CacheTimeToLive : null;
        result.CacheScope = supportsCachingHints ? CacheScope.Private : null;
        return result;
    }

    /// <summary>
    /// Classifies a resource-handler failure via <see cref="ToolErrorHandler.ClassifyError"/>
    /// (the same sanitized category/remediation source the tool surface uses) and maps the
    /// category onto the protocol-correct JSON-RPC error code for the request's negotiated era.
    /// </summary>
    private static McpProtocolException TranslateToProtocolError(
        Exception ex, RequestContext<ReadResourceRequestParams> context)
    {
        // McpToolException is a labelling wrapper some handlers use for non-JSON MIME types;
        // classify its cause so a wrapped KeyNotFoundException still maps to not-found
        // semantics instead of the InternalError fallback.
        var cause = ex is McpToolException { InnerException: { } inner } ? inner : ex;

        var uri = context.Params?.Uri ?? "<unknown>";
        var info = ToolErrorHandler.ClassifyError(cause, uri);
        var code = MapErrorCode(info.Category, RequestProtocolFeatureGate.UseInvalidParamsForMissingResource(context));
        return new McpProtocolException(BuildSanitizedMessage(info, uri), code);
    }

    private static McpErrorCode MapErrorCode(string category, bool useInvalidParamsForMissingResource) =>
        category switch
        {
            ToolErrorHandler.ErrorCategories.NotFound or
            ToolErrorHandler.ErrorCategories.FileNotFound or
            ToolErrorHandler.ErrorCategories.DirectoryNotFound or
            ToolErrorHandler.ErrorCategories.WorkspaceEvicted =>
                useInvalidParamsForMissingResource ? McpErrorCode.InvalidParams : McpErrorCode.ResourceNotFound,
            ToolErrorHandler.ErrorCategories.InvalidArgument => McpErrorCode.InvalidParams,
            _ => McpErrorCode.InternalError,
        };

    private static string BuildSanitizedMessage(ToolErrorHandler.ErrorInfo info, string uri)
    {
        // info.Message is already a sanitized remediation template (BuildSafe* helpers /
        // PublicExceptionDetailPolicy) — raw exception detail stays server-side.
        var correlationId = info.CorrelationId ?? RequestCorrelationContext.Current ?? "unavailable";
        var paramSuffix = string.IsNullOrEmpty(info.ParamName) ? string.Empty : $"; param: {info.ParamName}";
        return $"{info.Category}: {info.Message} (resource: {uri}{paramSuffix}; correlationId: {correlationId})";
    }
}
