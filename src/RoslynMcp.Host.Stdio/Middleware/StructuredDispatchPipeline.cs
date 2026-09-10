using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Runs pre-bind argument normalization, workspace identity recovery, and the bound tool
/// dispatch. It returns explicit early-terminal outcomes so terminal formatting remains solely
/// owned by <see cref="StructuredResultProjector"/>.
/// </summary>
internal static class StructuredDispatchPipeline
{
    internal readonly record struct DispatchOutcome(CallToolResult Result, bool IsEarlyTerminal);

    internal static async ValueTask<CallToolResult> ExecuteAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        var toolName = context.Params?.Name ?? "unknown";
        var logger = context.Services?
            .GetService<ILoggerFactory>()?
            .CreateLogger("RoslynMcp.StructuredCallToolFilter");
        var exceptionReporter = context.Services?.GetService<IUnexpectedExceptionReporter>();

        using var metricsScope = AmbientGateMetrics.BeginRequest();
        using var serverScope = RequestMcpServerContext.Begin(context.Server);
        using var loggingScope = logger?.BeginScope(
            "correlationId={CorrelationId}",
            RequestCorrelationContext.Current ?? "unavailable");
        var stopwatch = Stopwatch.StartNew();

        return await StructuredResultProjector.ExecuteAsync(
            context,
            toolName,
            logger,
            exceptionReporter,
            stopwatch,
            () => DispatchAsync(context, next, toolName, logger, exceptionReporter, stopwatch, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async ValueTask<DispatchOutcome> DispatchAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string toolName,
        ILogger? logger,
        IUnexpectedExceptionReporter? exceptionReporter,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (context.Params is not null)
        {
            context.Params.Arguments = LspSourceLocationArgumentNormalizer.Normalize(
                toolName,
                context.Params.Arguments);
        }

        // Detect missing workspace_load.path before the SDK binder turns it into a generic
        // arguments failure that cannot safely be mapped back to an allowlisted input field.
        var workspacePathRecovery = await StructuredCallElicitationCoordinator
            .TryRecoverMissingWorkspacePathAsync(context, next, logger, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (workspacePathRecovery is not null)
        {
            return new DispatchOutcome(
                StructuredResultProjector.ProjectRecoveredResult(
                    context,
                    workspacePathRecovery,
                    toolName,
                    exceptionReporter,
                    stopwatch),
                IsEarlyTerminal: true);
        }

        if (StructuredWorkspaceResolver.IsApplicable(context, toolName))
        {
            if (StructuredWorkspaceResolver.IsWorkspaceIdMissingOrBlank(context.Params?.Arguments) &&
                StructuredWorkspaceResolver.HasAuthoritativeWorkspacePathResponse(context))
            {
                // An accepted modern path response wins over request state and ambient workspace
                // state. A malformed or declined response is authoritative too, so fall through
                // to the binder rather than silently replacing the operator's input.
                var recovered = await TryRecoverMissingWorkspaceIdFromPathAsync(
                    context,
                    next,
                    toolName,
                    logger,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (recovered is not null)
                {
                    return new DispatchOutcome(
                        StructuredResultProjector.ProjectRecoveredResult(
                            context,
                            recovered,
                            toolName,
                            exceptionReporter,
                            stopwatch),
                        IsEarlyTerminal: true);
                }
            }
            else
            {
                var resolution = await StructuredWorkspaceResolver.ResolveAsync(
                    context,
                    toolName,
                    logger,
                    cancellationToken,
                    (dispatchToolName, arguments) => InvokeRegisteredToolWithTemporaryArgumentsAsync(
                        context,
                        dispatchToolName,
                        arguments,
                        cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (resolution.TerminalException is not null)
                {
                    return new DispatchOutcome(
                        StructuredResultProjector.ProjectEarlyExceptionResult(
                            context,
                            toolName,
                            resolution.TerminalException,
                            exceptionReporter,
                            stopwatch),
                        IsEarlyTerminal: true);
                }

                if (resolution.ShouldTryPathRecovery &&
                    StructuredWorkspaceResolver.IsWorkspaceIdMissingOrBlank(context.Params?.Arguments) &&
                    ElicitationChoicePrompt.SupportsElicitation(context))
                {
                    var recovered = await TryRecoverMissingWorkspaceIdFromPathAsync(
                        context,
                        next,
                        toolName,
                        logger,
                        cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (recovered is not null)
                    {
                        return new DispatchOutcome(
                            StructuredResultProjector.ProjectRecoveredResult(
                                context,
                                recovered,
                                toolName,
                                exceptionReporter,
                                stopwatch),
                            IsEarlyTerminal: true);
                    }
                }
            }
        }

        var result = await next(context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new DispatchOutcome(result, IsEarlyTerminal: false);
    }

    private static Task<CallToolResult?> TryRecoverMissingWorkspaceIdFromPathAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string toolName,
        ILogger? logger,
        CancellationToken cancellationToken) =>
        StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
            toolName,
            context.Params?.Arguments is null
                ? null
                : new Dictionary<string, JsonElement>(context.Params.Arguments, StringComparer.Ordinal),
            request => RequestScopedInputAdapter.RequestElicitationAsResultAsync(
                context,
                RequestScopedInputAdapter.WorkspacePathInputRequestKey,
                request,
                logger,
                cancellationToken),
            (dispatchToolName, arguments) =>
                string.Equals(dispatchToolName, toolName, StringComparison.Ordinal)
                    ? StructuredCallElicitationCoordinator.DispatchWithTemporaryArgumentsAsync(
                        context,
                        next,
                        dispatchToolName,
                        arguments,
                        cancellationToken)
                    : InvokeRegisteredToolWithTemporaryArgumentsAsync(
                        context,
                        dispatchToolName,
                        arguments,
                        cancellationToken),
            logger,
            cancellationToken);

    /// <summary>
    /// Invokes a named registered tool for cross-tool recovery. The request filter's bound
    /// delegate cannot be rerouted by temporarily changing <c>Params.Name</c>.
    /// </summary>
    private static async Task<CallToolResult> InvokeRegisteredToolWithTemporaryArgumentsAsync(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var tool = context.Services?
            .GetService<IOptions<McpServerOptions>>()?
            .Value
            .ToolCollection?
            .SingleOrDefault(candidate =>
                string.Equals(candidate.ProtocolTool.Name, toolName, StringComparison.Ordinal));
        if (tool is null)
        {
            throw new InvalidOperationException(
                $"Registered MCP tool '{toolName}' is unavailable for internal recovery dispatch.");
        }

        var originalToolName = context.Params!.Name;
        var originalArguments = context.Params.Arguments;
        try
        {
            context.Params.Name = toolName;
            context.Params.Arguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            var result = await tool.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (InputRequiredException inputRequired)
        {
            RequestStateCodec.PreserveWorkspaceId(
                inputRequired,
                context.Params.Arguments,
                ElicitationAllowlistPolicy.WorkspaceIdParameterName);
            throw;
        }
        finally
        {
            context.Params.Name = originalToolName;
            context.Params.Arguments = originalArguments;
        }
    }
}
