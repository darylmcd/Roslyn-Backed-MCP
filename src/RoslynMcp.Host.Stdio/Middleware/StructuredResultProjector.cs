using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Owns terminal structured-call result handling: elapsed metrics, wire-era shaping, content
/// metadata, error classification, safe formatting, and observability.
/// </summary>
internal static class StructuredResultProjector
{
    private static readonly EventId ToolCompletedEvent = new(2101, "ToolCompleted");
    private static readonly EventId ToolCancelledEvent = new(2102, "ToolCancelled");
    private static readonly EventId ToolInputRequiredEvent = new(2103, "ToolInputRequired");
    private static readonly EventId ToolFailedEvent = new(2104, "ToolFailed");

    internal static async ValueTask<CallToolResult> ExecuteAsync(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter,
        Stopwatch stopwatch,
        Func<ValueTask<StructuredDispatchPipeline.DispatchOutcome>> dispatchAsync)
    {
        try
        {
            var outcome = await dispatchAsync().ConfigureAwait(false);
            return outcome.IsEarlyTerminal
                ? outcome.Result
                : ProjectSuccessfulResult(
                    context,
                    outcome.Result,
                    toolName,
                    logger,
                    exceptionReporter,
                    stopwatch);
        }
        catch (OperationCanceledException)
        {
            var elapsedMs = StopAndRecordElapsed(stopwatch);
            logger?.LogWarning(
                ToolCancelledEvent,
                "Tool {ToolName} ended; outcome={Outcome}; elapsedMs={ElapsedMs}",
                toolName,
                "cancelled",
                elapsedMs);
            throw;
        }
        catch (InputRequiredException inputRequired)
        {
            // MRTR input-required is a protocol result, not a failed tool call. Preserve an
            // already resolved workspace identity for the client's next request.
            RequestStateCodec.PreserveWorkspaceId(
                inputRequired,
                context.Params?.Arguments,
                ElicitationAllowlistPolicy.WorkspaceIdParameterName);
            var elapsedMs = StopAndRecordElapsed(stopwatch);
            logger?.LogInformation(
                ToolInputRequiredEvent,
                "Tool {ToolName} ended; outcome={Outcome}; elapsedMs={ElapsedMs}",
                toolName,
                "input-required",
                elapsedMs);
            throw;
        }
        catch (Exception exception)
        {
            var elapsedMs = StopAndRecordElapsed(stopwatch);
            // Classify once. The resulting ErrorInfo determines both log/report severity and the
            // public formatter, eliminating the prior divergent second classification path.
            var errorInfo = ToolErrorHandler.ClassifyError(exception, toolName);
            var isInternalError = errorInfo.Category == ToolErrorHandler.ErrorCategories.InternalError;
            if (isInternalError && exceptionReporter is not null)
            {
                exceptionReporter.ReportUnexpected(exception, UnexpectedExceptionCategory.ToolCall);
            }

            logger?.Log(
                isInternalError ? LogLevel.Error : LogLevel.Warning,
                ToolFailedEvent,
                "Tool {ToolName} failed; outcome={Outcome}; elapsedMs={ElapsedMs}",
                toolName,
                isInternalError ? "unexpected-error" : "expected-error",
                elapsedMs);
            return ApplyProtocolResultShape(
                context,
                BuildErrorResult(toolName, exception, exceptionReporter, errorInfo));
        }
    }

    internal static CallToolResult BuildErrorResult(
        string toolName,
        Exception exception,
        IUnexpectedExceptionReporter? exceptionReporter = null) =>
        BuildErrorResult(
            toolName,
            exception,
            exceptionReporter,
            ToolErrorHandler.ClassifyError(exception, toolName));

    internal static CallToolResult InjectMetaIntoContent(
        CallToolResult result,
        string toolName,
        IUnexpectedExceptionReporter? exceptionReporter = null) =>
        StructuredCallContentProjector.InjectMetaIntoContent(result, toolName, exceptionReporter);

    internal static CallToolResult ProjectRecoveredResult(
        RequestContext<CallToolRequestParams> context,
        CallToolResult result,
        string toolName,
        IUnexpectedExceptionReporter? exceptionReporter,
        Stopwatch stopwatch)
    {
        StopAndRecordElapsed(stopwatch);
        return InjectMetaIntoContent(
            ApplyProtocolResultShape(context, result),
            toolName,
            exceptionReporter);
    }

    internal static CallToolResult ProjectEarlyExceptionResult(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        Exception exception,
        IUnexpectedExceptionReporter? exceptionReporter,
        Stopwatch stopwatch)
    {
        StopAndRecordElapsed(stopwatch);
        return ApplyProtocolResultShape(
            context,
            BuildErrorResult(toolName, exception, exceptionReporter));
    }

    private static CallToolResult BuildErrorResult(
        string toolName,
        Exception exception,
        IUnexpectedExceptionReporter? exceptionReporter,
        ToolErrorHandler.ErrorInfo errorInfo)
    {
        var envelope = ToolErrorHandler.FormatErrorResponse(errorInfo, toolName, exception);
        var envelopeWithMeta = ToolErrorHandler.InjectMetaIfPossible(
            envelope,
            toolName,
            exceptionReporter);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = envelopeWithMeta }],
        };
    }

    private static CallToolResult ProjectSuccessfulResult(
        RequestContext<CallToolRequestParams> context,
        CallToolResult result,
        string toolName,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter,
        Stopwatch stopwatch)
    {
        var elapsedMs = StopAndRecordElapsed(stopwatch);
        logger?.LogInformation(
            ToolCompletedEvent,
            "Tool {ToolName} completed; outcome={Outcome}; elapsedMs={ElapsedMs}",
            toolName,
            "success",
            elapsedMs);
        return InjectMetaIntoContent(
            ApplyProtocolResultShape(context, result),
            toolName,
            exceptionReporter);
    }

    private static long StopAndRecordElapsed(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
        return stopwatch.ElapsedMilliseconds;
    }

    private static CallToolResult ApplyProtocolResultShape(
        RequestContext<CallToolRequestParams> context,
        CallToolResult result)
    {
        if (!RequestProtocolFeatureGate.SupportsJuly2026Features(context))
        {
            result.ResultType = null;
            if (result.StructuredContent is { ValueKind: not JsonValueKind.Object } structuredContent)
            {
                result.StructuredContent = JsonSerializer.SerializeToElement(
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["result"] = structuredContent,
                    });
            }
        }

        return result;
    }
}
