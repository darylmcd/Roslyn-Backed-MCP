using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Composition boundary for the structured <c>tools/call</c> pipeline.
/// </summary>
/// <remarks>
/// The request filter must keep its public factory shape because the SDK registers this delegate
/// directly. Resolution, recovery/dispatch, and terminal result projection live in focused
/// collaborators so this boundary does not become a second protocol implementation.
/// </remarks>
internal static class StructuredCallToolFilter
{
    /// <summary>
    /// Creates the request decorator around the SDK handler already bound to the selected tool.
    /// Temporary <c>Params.Name</c> mutations used during a recovered retry do not reroute that
    /// bound handler; cross-tool recovery is dispatched through the registered tool collection.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return (context, cancellationToken) =>
            StructuredDispatchPipeline.ExecuteAsync(context, next, cancellationToken);
    }

    // Retain the narrow internal test surface while its owners move to dedicated collaborators.
    // These delegates deliberately contain no pipeline behavior or error-classification logic.
    internal static Task<T> AwaitRecoveryStageAsync<T>(
        Task<T> stage,
        CancellationToken cancellationToken) =>
        StructuredWorkspaceResolver.AwaitRecoveryStageAsync(stage, cancellationToken);

    internal static CallToolResult BuildErrorResult(
        string toolName,
        Exception exception,
        IUnexpectedExceptionReporter? exceptionReporter = null) =>
        StructuredResultProjector.BuildErrorResult(toolName, exception, exceptionReporter);

    internal static CallToolResult InjectMetaIntoContent(
        CallToolResult result,
        string toolName,
        IUnexpectedExceptionReporter? exceptionReporter = null) =>
        StructuredResultProjector.InjectMetaIntoContent(result, toolName, exceptionReporter);
}
