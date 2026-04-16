using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The single error-handling and observability boundary for every <c>tools/call</c> the
/// server dispatches. Wired in <see cref="Program"/> via
/// <c>WithRequestFilters(b =&gt; b.AddCallToolFilter(StructuredCallToolFilter.Create))</c>,
/// this filter replaces the per-tool <c>ToolErrorHandler.ExecuteAsync(...)</c> wrappers
/// that historically lived inside each handler body.
///
/// <para><b>Why a filter and not an inside-handler wrapper:</b></para>
/// <para>
/// Pre-binding failures (missing required parameter, unknown parameter name, JSON
/// deserialization of <c>arguments</c>) are thrown by the SDK's reflection-based argument
/// binder BEFORE the tool method runs. A wrapper inside the handler body therefore cannot
/// observe them; the SDK would surface a bare <c>"An error occurred invoking '&lt;tool&gt;'."</c>
/// string with no diagnostic detail. <c>AddCallToolFilter</c> wraps the entire dispatcher
/// call, so it sees binding exceptions exactly the same as handler-thrown exceptions
/// (SDK PR <c>csharp-sdk#844</c>, shipped in 0.4.0-preview.3 and carried into 1.x).
/// </para>
///
/// <para><b>Governance alignment (MCP SEP-1303):</b></para>
/// <para>
/// Both binding and handler errors are returned as <see cref="CallToolResult"/> with
/// <see cref="CallToolResult.IsError"/> set to <see langword="true"/> so the LLM sees the
/// structured envelope and can self-correct on the next turn. JSON-RPC protocol errors
/// (<c>-32602</c> and friends) are reserved for the spec-mandated cases (unknown method,
/// malformed envelope) and handled by the SDK itself, not this filter.
/// </para>
///
/// <para><b>Observability:</b></para>
/// <para>
/// Opens an <see cref="AmbientGateMetrics"/> request scope around <c>next(...)</c> so the
/// workspace execution gate can record queue/hold/stale timings, and a wall-clock
/// <see cref="Stopwatch"/> captures end-to-end elapsed time. Both the success and error
/// paths inject the snapshot as a top-level <c>_meta</c> property on the returned JSON
/// envelope via <see cref="ToolErrorHandler.InjectMetaIfPossible"/>.
/// </para>
///
/// <para>
/// Reference: <c>ai_docs/references/mcp-server-best-practices.md</c>.
/// </para>
/// </summary>
internal static class StructuredCallToolFilter
{
    /// <summary>
    /// Decorator factory matching the SDK's <c>McpRequestFilter&lt;TParams, TResult&gt;</c>
    /// contract: receive <paramref name="next"/> (the dispatcher handler produced by
    /// <c>WithToolsFromAssembly</c>) and return a handler that wraps it with structured
    /// error classification and <c>_meta</c> observability.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async (context, cancellationToken) =>
        {
            var toolName = context.Params?.Name ?? "unknown";
            var logger = context.Services?
                .GetService<ILoggerFactory>()?
                .CreateLogger("RoslynMcp.StructuredCallToolFilter");

            using var metricsScope = AmbientGateMetrics.BeginRequest();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                RecordElapsed(stopwatch.ElapsedMilliseconds);
                logger?.LogInformation("Tool {ToolName} completed successfully", toolName);
                return InjectMetaIntoContent(result, toolName);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a cooperative signal, not a tool error. Let the SDK
                // translate it into the protocol-level cancellation envelope.
                logger?.LogWarning("Tool {ToolName} was cancelled", toolName);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordElapsed(stopwatch.ElapsedMilliseconds);

                var level = IsInternalError(ex) ? LogLevel.Error : LogLevel.Warning;
                logger?.Log(level, ex, "Tool {ToolName} failed", toolName);

                return BuildErrorResult(toolName, ex);
            }
        };
    }

    /// <summary>
    /// Produces the <see cref="CallToolResult"/> envelope the filter emits when a tool call
    /// throws. Visible to tests so the classifier→envelope→<c>_meta</c> pipeline can be
    /// asserted without standing up a real <c>RequestContext</c> / <c>McpServer</c>.
    /// </summary>
    internal static CallToolResult BuildErrorResult(string toolName, Exception ex)
    {
        var envelope = ToolErrorHandler.ClassifyAndFormat(ex, toolName);
        var envelopeWithMeta = ToolErrorHandler.InjectMetaIfPossible(envelope, toolName);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = envelopeWithMeta }],
        };
    }

    /// <summary>
    /// Injects the gate-metrics snapshot as a top-level <c>_meta</c> property on the first
    /// text content block when the tool's JSON response is object-rooted. Arrays,
    /// primitives, and non-text content are returned unchanged — preserving the
    /// historical contract that e.g. <c>source_generated_documents</c>'s bare-array
    /// response shape remains stable across the filter migration. Exposed <c>internal</c>
    /// so tests can assert meta-injection behavior directly.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName)
    {
        if (result.Content is null || result.Content.Count == 0)
        {
            return result;
        }

        if (result.Content[0] is not TextContentBlock text || string.IsNullOrEmpty(text.Text))
        {
            return result;
        }

        var injected = ToolErrorHandler.InjectMetaIfPossible(text.Text, toolName);
        if (ReferenceEquals(injected, text.Text) || injected == text.Text)
        {
            // Nothing to change — skip the allocation so array-rooted and non-JSON
            // responses pass through byte-for-byte identical.
            return result;
        }

        var newContent = new List<ContentBlock>(result.Content.Count)
        {
            new TextContentBlock { Text = injected }
        };
        for (var i = 1; i < result.Content.Count; i++)
        {
            newContent.Add(result.Content[i]);
        }

        return new CallToolResult
        {
            IsError = result.IsError,
            Content = newContent,
            StructuredContent = result.StructuredContent,
        };
    }

    private static void RecordElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.ElapsedMs = elapsedMs;
        }
    }

    /// <summary>
    /// Predicate for logger severity: anything that <see cref="ToolErrorHandler"/>
    /// classifies as <c>InternalError</c> becomes an Error log; known categories
    /// (InvalidArgument, NotFound, Timeout, RateLimited, InvalidOperation,
    /// WorkspaceReloadedDuringCall, ...) stay at Warning since they are caller-correctable
    /// or recoverable. We approximate the classification shallowly here — a false Warning
    /// on a rare deep-domain error is preferable to re-running the full classifier twice.
    /// </summary>
    private static bool IsInternalError(Exception ex) =>
        ex is NullReferenceException
        or InvalidCastException
        or IndexOutOfRangeException
        or StackOverflowException
        or OutOfMemoryException;
}
