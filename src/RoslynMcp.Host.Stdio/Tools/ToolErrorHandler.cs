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
            return InjectMetaIfPossible(result);
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
            return InjectMetaIfPossible(FormatErrorResponse(info, source, ex));
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
            return InjectMetaIfPossible(result);
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
            return InjectMetaIfPossible(FormatErrorResponse(info, source, ex));
        }
    }

    /// <summary>
    /// Wraps a tool action with structured error handling. Returns errors as structured JSON content
    /// so MCP clients always receive actionable error details, regardless of SDK exception handling.
    /// </summary>
    public static async Task<string> ExecuteAsync(string toolName, Func<Task<string>> action, ILogger? auditLogger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        // P3: Open an ambient metrics scope so the workspace execution gate can record per-request
        // queue/hold timings, and we can merge them into the response JSON as `_meta` so clients
        // that don't surface MCP `notifications/message` (e.g. Claude Code) still get observability.
        using var metricsScope = AmbientGateMetrics.BeginRequest();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await action().ConfigureAwait(false);
            stopwatch.Stop();
            // backlog: reader-tool-elapsed-ms — every tool, reader or writer, gets the same
            // wall-clock timing in the response _meta block. Concurrency audits use this to
            // compute speedup ratios from inside the agent loop.
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            auditLogger?.LogInformation("Tool {ToolName} completed successfully", toolName);
            return InjectMetaIfPossible(result);
        }
        catch (OperationCanceledException)
        {
            auditLogger?.LogWarning("Tool {ToolName} was cancelled", toolName);
            throw; // Let MCP SDK handle cancellation natively
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (AmbientGateMetrics.Current is { } metrics)
            {
                metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
            }
            var info = ClassifyError(ex, toolName);
            auditLogger?.Log(
                info.Category == "InternalError" ? LogLevel.Error : LogLevel.Warning,
                ex, "Tool {ToolName} failed: {ErrorCategory}", toolName, info.Category);

            return InjectMetaIfPossible(FormatErrorResponse(info, toolName, ex));
        }
    }

    /// <summary>
    /// P3: Parse the JSON result and add a top-level <c>_meta</c> property carrying the gate
    /// metrics snapshot. Only injected when the root is a JSON object — non-object roots
    /// (arrays, primitives) are returned unchanged so we don't break consumers that expect a
    /// specific shape. On any parse failure the original string is returned unchanged so this
    /// never breaks a well-formed response.
    /// </summary>
    private static string InjectMetaIfPossible(string json)
    {
        var snapshot = AmbientGateMetrics.Snapshot();
        if (snapshot is null) return json;

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj) return json;

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

    private static ErrorInfo ClassifyError(Exception ex, string toolName)
    {
        // Walk the handler dictionary for exact or assignable type match
        foreach (var (type, handler) in ErrorHandlers)
        {
            if (type.IsAssignableFrom(ex.GetType()))
                return handler(ex, toolName);
        }

        // MCP SDK parameter binding failures: the framework wraps deserialization errors
        // in a generic exception. Surface the inner JsonException or ArgumentException with
        // actionable hints about the correct JSON property names.
        if (ex.InnerException is System.Text.Json.JsonException jsonEx)
        {
            return new("InvalidArgument",
                $"Parameter binding failed: {jsonEx.Message}. " +
                "Check that JSON property names match the tool schema exactly (camelCase).");
        }
        if (ex.InnerException is ArgumentException argEx && ex.GetType().Name.Contains("Invocation", StringComparison.OrdinalIgnoreCase))
        {
            return new("InvalidArgument",
                $"Parameter validation failed: {argEx.Message}. " +
                "Check that all required parameters are provided and values match the expected types.");
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

    private static string FormatErrorResponse(ErrorInfo info, string toolName, Exception ex)
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

    private readonly record struct ErrorInfo(string Category, string Message);
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
