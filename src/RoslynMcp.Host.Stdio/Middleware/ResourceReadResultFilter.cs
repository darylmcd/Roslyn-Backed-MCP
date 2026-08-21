using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
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
        // Seed the classifier with the ambient per-message correlation id so the unexpected-
        // failure fallback stamps the REAL id instead of its own "unavailable" sentinel (which,
        // being non-null, would win the ?? in BuildSanitizedMessage and make the wire id dead).
        var info = ToolErrorHandler.ClassifyError(cause, uri, RequestCorrelationContext.Current);
        var code = MapErrorCode(info.Category, RequestProtocolFeatureGate.UseInvalidParamsForMissingResource(context));
        return new McpProtocolException(BuildSanitizedMessage(info, uri), code);
    }

    /// <summary>
    /// Maps every currently declared <see cref="ToolErrorHandler.ClassifyError"/> category onto
    /// a wire code. Each known category gets an explicit arm, including those that resolve to
    /// <see cref="McpErrorCode.InternalError"/>. The trailing discard arm is a fail-safe because
    /// string switches are not exhaustive; category additions must still update this mapping.
    /// </summary>
    private static McpErrorCode MapErrorCode(string category, bool useInvalidParamsForMissingResource) =>
        category switch
        {
            // Missing-resource family: era-dependent (-32002 legacy, -32602 from 2026-07-28).
            ToolErrorHandler.ErrorCategories.NotFound or
            ToolErrorHandler.ErrorCategories.FileNotFound or
            ToolErrorHandler.ErrorCategories.DirectoryNotFound or
            ToolErrorHandler.ErrorCategories.WorkspaceEvicted =>
                useInvalidParamsForMissingResource ? McpErrorCode.InvalidParams : McpErrorCode.ResourceNotFound,
            // Caller-input fault: always -32602.
            ToolErrorHandler.ErrorCategories.InvalidArgument => McpErrorCode.InvalidParams,
            // Server-side / transport / lifecycle conditions. The caller's request was
            // well-formed, so these are server faults (-32603) with a retry hint in the
            // sanitized remediation text, not InvalidParams.
            ToolErrorHandler.ErrorCategories.StaleWorkspaceTransition or
            ToolErrorHandler.ErrorCategories.WorkspaceReloadedDuringCall or
            ToolErrorHandler.ErrorCategories.PreviewTokenStale or
            ToolErrorHandler.ErrorCategories.Timeout or
            ToolErrorHandler.ErrorCategories.Disconnected or
            ToolErrorHandler.ErrorCategories.RateLimited or
            ToolErrorHandler.ErrorCategories.InvalidOperation or
            ToolErrorHandler.ErrorCategories.PermissionDenied or
            ToolErrorHandler.ErrorCategories.InternalError => McpErrorCode.InternalError,
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
