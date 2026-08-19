using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The single error-handling and observability boundary for every <c>prompts/get</c> the server
/// dispatches. Wired in <see cref="Program"/> via
/// <c>WithRequestFilters(b =&gt; b.AddGetPromptFilter(GetPromptErrorFilter.Create))</c>, mirroring
/// the decorator shape of <see cref="StructuredCallToolFilter"/> for <c>tools/call</c>.
///
/// <para><b>Why prompt failures ride the JSON-RPC error channel:</b></para>
/// <para>
/// Unlike <c>tools/call</c> (whose result contract carries an <c>isError</c> envelope the LLM can
/// self-correct from, per MCP SEP-1303), <c>prompts/get</c> has no error shape inside a successful
/// result — a "prompt message describing the failure" is indistinguishable from real prompt
/// content. Unexpected prompt failures are therefore converted into a sanitized
/// <see cref="McpProtocolException"/> with <see cref="McpErrorCode.InternalError"/> (-32603) so
/// the client observes a protocol-level failure, never a fabricated user-role message.
/// </para>
///
/// <para><b>Sanitization contract:</b></para>
/// <para>
/// Unexpected exceptions are projected through
/// <see cref="PublicExceptionDetailPolicy.ProjectUnexpected"/> +
/// <see cref="IUnexpectedExceptionReporter"/> (category
/// <see cref="UnexpectedExceptionCategory.GetPrompt"/>): the client sees only a fixed category,
/// summary, remediation, and correlation id; the server-side observability sink retains the
/// exception type chain and stack-frame count but never the message, data, or raw stack text.
/// </para>
///
/// <para><b>Preserved semantics:</b></para>
/// <list type="bullet">
///   <item><see cref="OperationCanceledException"/> is rethrown untouched so the SDK translates it
///         into the protocol-level cancellation envelope (same as
///         <see cref="StructuredCallToolFilter"/>).</item>
///   <item><see cref="McpProtocolException"/> (unknown prompt name, SDK-level parameter
///         validation) is rethrown untouched, keeping the SDK's <c>InvalidParams</c>
///         contract.</item>
///   <item>Binding-stage <see cref="ArgumentException"/> / <see cref="JsonException"/> failures
///         are converted to <see cref="McpErrorCode.InvalidParams"/> so parameter mistakes stay
///         actionable instead of collapsing into <c>InternalError</c>.</item>
/// </list>
/// </summary>
internal static class GetPromptErrorFilter
{
    /// <summary>
    /// Decorator factory matching the SDK's request-filter contract: receive
    /// <paramref name="next"/> (the dispatcher handler produced by
    /// <c>WithPromptsFromAssembly</c>) and return a handler that wraps it with the sanitizing
    /// error boundary described on the class.
    /// </summary>
    public static McpRequestHandler<GetPromptRequestParams, GetPromptResult> Create(
        McpRequestHandler<GetPromptRequestParams, GetPromptResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async (context, cancellationToken) =>
        {
            var promptName = context.Params?.Name ?? "unknown";
            var logger = context.Services?
                .GetService<ILoggerFactory>()?
                .CreateLogger("RoslynMcp.GetPromptErrorFilter");

            try
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a cooperative signal, not a prompt error. Let the SDK
                // translate it into the protocol-level cancellation envelope.
                logger?.LogWarning("Prompt {PromptName} was cancelled", promptName);
                throw;
            }
            catch (McpProtocolException)
            {
                // Already protocol-shaped (unknown prompt, SDK parameter validation). The SDK's
                // messages carry no exception internals, so pass them through unchanged to keep
                // the InvalidParams contract intact.
                logger?.LogWarning("Prompt {PromptName} failed with a protocol error", promptName);
                throw;
            }
            catch (Exception ex)
            {
                throw TranslateException(
                    ex,
                    promptName,
                    context.Services?.GetService<IUnexpectedExceptionReporter>(),
                    logger);
            }
        };
    }

    /// <summary>
    /// Classifies a non-cancellation, non-protocol exception thrown by prompt binding or a prompt
    /// handler into the protocol exception the boundary throws in its place. Exposed for focused
    /// unit tests; the returned exception never carries the source exception's message, type
    /// names, inner chain, stack text, or paths except for the binding-validation shapes noted
    /// on the class.
    /// </summary>
    internal static McpProtocolException TranslateException(
        Exception exception,
        string promptName,
        IUnexpectedExceptionReporter? reporter,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case ArgumentException argumentException:
                // Binding-stage validation (missing/unknown required parameter). The binder's
                // message names the parameter, never the supplied value — keep it actionable.
                logger?.LogWarning(
                    "Prompt {PromptName} failed parameter validation: {Reason}",
                    promptName,
                    argumentException.Message);
                return new McpProtocolException(
                    $"Invalid parameters for prompt '{promptName}': {argumentException.Message}",
                    McpErrorCode.InvalidParams);

            case JsonException:
                // Argument deserialization failure. JsonException messages can echo payload
                // fragments, so emit a fixed description instead of the raw message.
                logger?.LogWarning(
                    "Prompt {PromptName} failed argument deserialization", promptName);
                return new McpProtocolException(
                    $"Invalid parameters for prompt '{promptName}': the supplied arguments could not be deserialized.",
                    McpErrorCode.InvalidParams);
        }

        var details = UnexpectedExceptionReporting.Report(
            reporter,
            exception,
            UnexpectedExceptionCategory.GetPrompt);
        logger?.LogError(
            "Prompt {PromptName} failed with unexpected-error; correlationId={CorrelationId}",
            promptName,
            details.Public.CorrelationId);
        return new McpProtocolException(
            $"{details.Public.Summary} {details.Public.Remediation} (correlationId: {details.Public.CorrelationId})",
            McpErrorCode.InternalError);
    }
}
