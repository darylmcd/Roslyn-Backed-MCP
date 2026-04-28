using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Tools;

internal static class MetaSerializer
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

internal static class ToolErrorHandler
{
    private static readonly Dictionary<Type, Func<Exception, string, ErrorInfo>> ErrorHandlers = new()
    {
        // autoreload-cascade-stdio-host-crash: wraps a state-read that raced with (or followed)
        // an auto-reload transition. Registered BEFORE the generic Exception handlers so the
        // structured category wins over the dictionary walk's InvalidOperation fallback.
        [typeof(StaleWorkspaceTransitionException)] = (ex, _) => new("StaleWorkspaceTransition",
            $"Workspace is transitioning between snapshots: {ex.Message}"),
        // mcp-error-category-workspace-evicted-on-host-recycle: a workspace lookup miss that
        // originated from a host recycle (graceful prior-process exit) or an explicit Close
        // surfaces as WorkspaceEvicted with both serverStartedAt and (when known) the prior
        // workspaceLoadedAt embedded in the message. WorkspaceEvictedException derives from
        // KeyNotFoundException so existing catch-sites still observe it as a lookup miss; it
        // is registered BEFORE the base KeyNotFoundException entry below so the dictionary
        // walk's IsAssignableFrom check produces the more-specific category.
        [typeof(WorkspaceEvictedException)] = (ex, _) =>
        {
            var evicted = (WorkspaceEvictedException)ex;
            var loadedAtSegment = evicted.WorkspaceLoadedAtUtc is { } loadedAt
                ? $" workspaceLoadedAt={loadedAt:O};"
                : string.Empty;
            return new("WorkspaceEvicted",
                $"{ex.Message} serverStartedAt={evicted.ServerStartedAtUtc:O};{loadedAtSegment}");
        },
        [typeof(FileNotFoundException)] = (ex, _) => new("FileNotFound",
            $"File not found: {ex.Message}. Verify the file path is absolute and the file exists on disk. " +
            "If the workspace was recently reloaded, the file may have been removed."),
        [typeof(DirectoryNotFoundException)] = (ex, _) => new("DirectoryNotFound",
            $"Directory not found: {ex.Message}. Verify the directory path is absolute and exists on disk."),
        [typeof(KeyNotFoundException)] = (ex, _) => new("NotFound",
            $"Not found: {ex.Message}. Ensure the workspace is loaded (workspace_load) and the identifier is correct."),
        [typeof(ArgumentException)] = (ex, _) => new("InvalidArgument",
            $"Invalid argument: {ex.Message}. Check parameter types and values match the tool schema."),
        [typeof(TimeoutException)] = (ex, _) => new("Timeout",
            $"Timed out: {ex.Message}. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or " +
            "ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS."),
        [typeof(InvalidOperationException)] = (ex, _) => ex.Message.Contains("Rate limit")
            ? new("RateLimited", ex.Message)
            : new("InvalidOperation",
                ShouldSuggestReloadAfterInvalidOperation(ex.Message)
                    ? $"Invalid operation: {ex.Message}. The workspace may need to be reloaded (workspace_reload) if the state is stale."
                    : $"Invalid operation: {ex.Message}."),
    };

    /// <summary>
    /// Wraps a synchronous resource action with structured error handling, returning the same
    /// error envelope shape as <see cref="ExecuteAsync"/> so JSON-MIME resources surface a
    /// well-formed error document with the correct <c>tool</c> field instead of bubbling an
    /// exception that the framework labels as <c>tool: "unknown"</c>. The <paramref name="source"/>
    /// is the resource URI (or its template form) that originated the call.
    /// </summary>
    public static string ExecuteResource(string source, Func<string> action, ILogger? auditLogger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        using var metricsScope = AmbientGateMetrics.BeginRequest();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = action();
            stopwatch.Stop();
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            return InjectMetaIfPossible(result, source);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            var info = ClassifyError(ex, source);
            auditLogger?.Log(
                info.Category == "InternalError" ? LogLevel.Error : LogLevel.Warning,
                ex, "Resource {Source} failed: {ErrorCategory}", source, info.Category);
            return InjectMetaIfPossible(FormatErrorResponse(info, source, ex), source);
        }
    }

    /// <summary>
    /// Async variant of <see cref="ExecuteResource"/> for awaitable resource handlers.
    /// </summary>
    public static async Task<string> ExecuteResourceAsync(string source, Func<Task<string>> action, ILogger? auditLogger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        using var metricsScope = AmbientGateMetrics.BeginRequest();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            stopwatch.Stop();
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            return InjectMetaIfPossible(result, source);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            var info = ClassifyError(ex, source);
            auditLogger?.Log(
                info.Category == "InternalError" ? LogLevel.Error : LogLevel.Warning,
                ex, "Resource {Source} failed: {ErrorCategory}", source, info.Category);
            return InjectMetaIfPossible(FormatErrorResponse(info, source, ex), source);
        }
    }

    /// <summary>
    /// P3: Parse the JSON result and add a top-level <c>_meta</c> property carrying the gate
    /// metrics snapshot. Only injected when the root is a JSON object — non-object roots
    /// (arrays, primitives) are returned unchanged so we don't break consumers that expect a
    /// specific shape. On any parse failure the original string is returned unchanged so this
    /// never breaks a well-formed response. Exposed <c>internal</c> so the
    /// <c>StructuredCallToolFilter</c> can inject <c>_meta</c> on both success and error paths.
    /// </summary>
    internal static string InjectMetaIfPossible(string json, string? source = null)
    {
        var snapshot = AmbientGateMetrics.Snapshot();
        if (snapshot is null)
        {
            System.Diagnostics.Trace.TraceWarning(
                "_meta injection skipped for {0}: no ambient metrics scope", source ?? "unknown");
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "_meta injection skipped for {0}: response root is not a JSON object", source ?? "unknown");
                return json;
            }

            var metaNode = JsonSerializer.SerializeToNode(snapshot, MetaSerializer.CamelCase);
            if (metaNode is null) return json;

            obj["_meta"] = metaNode;
            return obj.ToJsonString(JsonDefaults.Indented);
        }
        catch
        {
            // Best-effort: never break a tool response over an injection failure.
            return json;
        }
    }

    internal static ErrorInfo ClassifyError(Exception ex, string toolName)
    {
        // mcp-parameter-validation-error-messages: detect parameter-binding failures
        // in BOTH shapes the runtime can deliver them:
        //   1. Wrapped in a generic invocation envelope — legacy reflection dispatch surfaces
        //      parameter problems as InvalidOperationException or TargetInvocationException
        //      with the real cause as ImmediateInnerException. The dictionary entry for
        //      InvalidOperationException would otherwise mask this as a generic "InvalidOperation".
        //   2. Raw (unwrapped) — once wired through ModelContextProtocol's AddCallToolFilter
        //      (SDK PR csharp-sdk#844, shipped in 0.4.0-preview.3 and carried into 1.x), the
        //      filter pipeline propagates pre-binding ArgumentException/JsonException/etc.
        //      directly without an invocation wrapper.
        //
        // Both shapes produce the same structured InvalidArgument envelope. The helper
        // TryClassifyBindingLike runs against the inner exception when there's an invocation
        // wrapper, otherwise against the exception itself. Legitimate internal errors
        // (AggregateException → InvalidCastException → ArgumentException buried deep in
        // domain code) are NOT misclassified because the shallow walk only inspects the
        // immediate exception at each level.
        if (IsInvocationWrapper(ex) && ex.InnerException is { } directInner)
        {
            if (TryClassifyBindingLike(directInner, out var wrappedInfo))
                return wrappedInfo;
        }
        else if (TryClassifyBindingLike(ex, out var rawInfo))
        {
            return rawInfo;
        }

        // symbol-impact-sweep-race-with-auto-reload: when a symbol fails to resolve AND the
        // request's gate reported it had to auto-reload the workspace mid-call, surface a
        // race-specific envelope instead of the generic "NotFound" that implies the symbol
        // itself is gone. The pre-reload handle/metadata-name may still be valid; callers
        // just need to re-resolve against the fresh compilation and retry. Scope is
        // deliberately narrow — any KeyNotFoundException outside a reload window still
        // falls through to the generic NotFound handler.
        //
        // mcp-error-category-workspace-evicted-on-host-recycle: WorkspaceEvictedException
        // derives from KeyNotFoundException for catch-site compatibility, so the generic
        // `is KeyNotFoundException` test above would otherwise re-classify a recycle-eviction
        // as WorkspaceReloadedDuringCall whenever the in-call gate reported a stale auto-
        // reload. Exclude the more-specific eviction type here so the dictionary handler
        // below (registered with explicit `typeof(WorkspaceEvictedException)`) wins.
        if (ex is KeyNotFoundException && ex is not WorkspaceEvictedException &&
            AmbientGateMetrics.Current?.StaleAction == "auto-reloaded")
        {
            return new("WorkspaceReloadedDuringCall",
                $"Workspace was auto-reloaded during this call; {ex.Message} The symbol handle " +
                "or metadata name may target the pre-reload compilation. Re-resolve the symbol " +
                "(e.g. via symbol_search, symbol_info, or a fresh position-based locator) and retry. " +
                "See _meta.staleReloadMs for how long the reload held the request.");
        }

        // Walk the handler dictionary for exact or assignable type match
        foreach (var (type, handler) in ErrorHandlers)
        {
            if (type.IsAssignableFrom(ex.GetType()))
                return handler(ex, toolName);
        }

        // Fallback: unexpected error — include inner exception chain for diagnosis
        var innerMessages = GetInnerExceptionChain(ex);
        var detail = string.IsNullOrEmpty(innerMessages)
            ? $"{ex.GetType().Name}: {ex.Message}"
            : $"{ex.GetType().Name}: {ex.Message} --> {innerMessages}";

        return new("InternalError",
            $"Internal error in {toolName}: {detail}. " +
            "If this persists, try reloading the workspace (workspace_reload).");
    }

    /// <summary>
    /// Classifies <paramref name="ex"/> against the five known parameter-binding exception
    /// shapes (<see cref="System.Text.Json.JsonException"/>, <see cref="ArgumentNullException"/>,
    /// <see cref="ArgumentOutOfRangeException"/>, <see cref="ArgumentException"/>,
    /// <see cref="FormatException"/>) and produces an <c>InvalidArgument</c> envelope that
    /// names the offending parameter where possible. Returns <see langword="false"/> when
    /// the exception is not a binding-like shape, leaving the caller to fall through to
    /// the dictionary or the default <c>InternalError</c> branch.
    /// </summary>
    private static bool TryClassifyBindingLike(Exception ex, out ErrorInfo info)
    {
        switch (ex)
        {
            case System.Text.Json.JsonException jsonEx:
                info = new("InvalidArgument",
                    $"Parameter binding failed (JSON deserialization): {jsonEx.Message}. " +
                    "Check that JSON property names match the tool schema exactly (camelCase).");
                return true;

            case ArgumentNullException nullArgEx:
                info = new("InvalidArgument",
                    $"Required parameter '{nullArgEx.ParamName ?? "<unknown>"}' is missing or null. " +
                    "Provide a value for this parameter and retry.");
                return true;

            case ArgumentOutOfRangeException rangeEx:
                info = new("InvalidArgument",
                    $"Parameter '{rangeEx.ParamName ?? "<unknown>"}' has an out-of-range value: {rangeEx.Message}. " +
                    "Check the tool schema for the accepted range.");
                return true;

            case ArgumentException argEx:
                info = new("InvalidArgument",
                    $"Parameter '{argEx.ParamName ?? "<unknown>"}' is invalid: {argEx.Message}. " +
                    "Check that all required parameters are provided and values match the expected types.");
                return true;

            case FormatException fmtEx:
                info = new("InvalidArgument",
                    $"Parameter format error: {fmtEx.Message}. " +
                    "Check that values match the expected format (e.g., integers, booleans, paths).");
                return true;
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Facade for the StructuredCallToolFilter: classify an exception and return a fully
    /// formatted JSON error envelope (without <c>_meta</c>; inject that separately). Wraps
    /// <see cref="ClassifyError"/> + <see cref="FormatErrorResponse"/> so callers don't need
    /// access to the private <see cref="ErrorInfo"/> type.
    /// </summary>
    internal static string ClassifyAndFormat(Exception ex, string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        var info = ClassifyError(ex, toolName);
        return FormatErrorResponse(info, toolName, ex);
    }

    /// <summary>
    /// Returns true when the exception type indicates a generic SDK/reflection invocation
    /// wrapper that hides the real cause in InnerException. Used by the parameter-binding
    /// detector to scope its inner-exception check to legitimate wrappers (and avoid
    /// misclassifying domain errors that happen to have an Argument-like exception in their
    /// chain).
    /// </summary>
    private static bool IsInvocationWrapper(Exception ex)
    {
        if (ex is System.Reflection.TargetInvocationException) return true;
        // The MCP SDK + Microsoft.Extensions.* hosting wrap with InvalidOperationException
        // when a tool method throws during binding. Walk type names rather than concrete
        // types so the SDK can rev internal exception classes without breaking us.
        var name = ex.GetType().Name;
        return name.Contains("Invocation", StringComparison.OrdinalIgnoreCase)
            || (name == nameof(InvalidOperationException) && ex.Message.Contains("invocation", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only suggest workspace_reload when the message plausibly indicates stale or missing workspace state.
    /// </summary>
    private static bool ShouldSuggestReloadAfterInvalidOperation(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found in workspace", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Document not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("workspace changed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("workspace version", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("outside the workspace", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("preview token", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetInnerExceptionChain(Exception ex)
    {
        var parts = new List<string>();
        var inner = ex.InnerException;
        while (inner is not null && parts.Count < 3)
        {
            parts.Add($"{inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
        }
        return string.Join(" --> ", parts);
    }

    private static string GetAbbreviatedStackTrace(Exception ex)
    {
        if (ex.StackTrace is null) return string.Empty;
        var frames = ex.StackTrace
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .ToArray();
        return string.Join("\n", frames);
    }

    internal static string FormatErrorResponse(ErrorInfo info, string toolName, Exception ex)
    {
        if (info.Category == "InternalError")
        {
            var error = new
            {
                error = true,
                category = info.Category,
                tool = toolName,
                message = info.Message,
                exceptionType = ex.GetType().Name,
                stackTrace = GetAbbreviatedStackTrace(ex),
            };
            return JsonSerializer.Serialize(error, JsonDefaults.Indented);
        }
        else
        {
            var error = new
            {
                error = true,
                category = info.Category,
                tool = toolName,
                message = info.Message,
                exceptionType = ex.GetType().Name,
            };
            return JsonSerializer.Serialize(error, JsonDefaults.Indented);
        }
    }

    internal readonly record struct ErrorInfo(string Category, string Message);
}

/// <summary>
/// Exception type for tool errors. Can be used by resource handlers and other non-tool contexts
/// where structured JSON error responses are not applicable. The optional
/// <see cref="OriginatingSource"/> property carries the originating tool name or resource URI
/// so the MCP framework can label error envelopes with the actual originator instead of
/// "unknown".
/// </summary>
public sealed class McpToolException : Exception
{
    /// <summary>
    /// Originating tool name or resource URI (e.g., <c>"workspace_status"</c>,
    /// <c>"roslyn://workspaces"</c>). When null, defaults to "unknown" in the rendered envelope.
    /// Named with the <c>Originating</c> prefix to avoid colliding with the inherited
    /// <see cref="Exception.Source"/> property (which is the assembly name by default).
    /// </summary>
    public string? OriginatingSource { get; }

    public McpToolException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    public McpToolException(string source, string message, Exception? inner = null) : base(message, inner)
    {
        OriginatingSource = source;
    }
}
