using System.Diagnostics;
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
///   <item>SDK-origin <see cref="ArgumentException"/> and <see cref="JsonException"/> binding
///         failures receive a fixed <see cref="McpErrorCode.InvalidParams"/> response. The same
///         exception types from handler code use the sanitized unexpected-error path.</item>
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
    /// names, inner chain, stack text, or paths. SDK versions that already protocol-shape a
    /// binding failure are passed through by <see cref="Create"/> before this method is called;
    /// plain SDK binding exceptions are classified below.
    /// </summary>
    internal static McpProtocolException TranslateException(
        Exception exception,
        string promptName,
        IUnexpectedExceptionReporter? reporter,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsSdkParameterBindingFailure(exception))
        {
            logger?.LogWarning("Prompt {PromptName} failed SDK parameter binding", promptName);
            return new McpProtocolException(
                $"Invalid parameters for prompt '{promptName}'. " +
                "Provide every required argument using the advertised parameter types.",
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

    /// <summary>
    /// The pinned SDK performs prompt binding and handler invocation inside one dispatcher and
    /// uses the same public exception types for both stages. Identify binding by its SDK-owned
    /// invocation frame instead of by exception type, parameter name, or message; those latter
    /// values are all controllable by a handler. Wire tests pin both sides of this distinction so
    /// an SDK implementation change fails closed as <c>InternalError</c> rather than disclosing a
    /// handler message.
    /// </summary>
    private static bool IsSdkParameterBindingFailure(Exception exception)
    {
        if (exception is not (ArgumentException or JsonException))
            return false;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                var declaringType = frame.GetMethod()?.DeclaringType;
                if (declaringType?.Assembly.GetName().Name == "Microsoft.Extensions.AI.Abstractions" &&
                    declaringType.FullName?.StartsWith(
                        "Microsoft.Extensions.AI.AIFunctionFactory+ReflectionAIFunctionDescriptor",
                        StringComparison.Ordinal) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
