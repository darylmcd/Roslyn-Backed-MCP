using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The elicitation/retry orchestration layer extracted from <see cref="StructuredCallToolFilter"/>.
/// Owns the <em>how</em> of asking the user for a missing value and re-dispatching the original
/// call — the pre-bind workspace-path recovery
/// (<see cref="TryRecoverMissingWorkspacePathAsync"/>) and the workspaceId-specific recover-load-retry loop
/// (<see cref="TryRecoverMissingWorkspaceIdAsync"/>). Whether a parameter <em>may</em> be elicited
/// stays in <see cref="ElicitationAllowlistPolicy"/>; the transport-era decision (MRTR
/// input-required signal vs direct nested <c>elicitation/create</c>) is delegated to
/// <see cref="RequestScopedInputAdapter"/>; metrics stay in
/// <see cref="CallMetricsRecorder"/>; the pre-dispatch auto-resolve/auto-load flow and
/// error-envelope construction stay in the filter. The shared select-from-N picker is NOT owned
/// here — its canonical (and only) home is
/// <see cref="ElicitationChoicePrompt.TryElicitChoiceAsync"/>, in the cycle-free
/// <c>RoslynMcp.Host.Stdio.Elicitation</c> namespace so <c>Tools</c> can call it without importing
/// <c>Middleware</c>.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> invokes this collaborator directly.
/// <see cref="DispatchWithTemporaryArgumentsAsync"/> remains internal for the patched retry of
/// the already-bound original tool; <see cref="TryExtractWorkspaceId"/> is shared with the
/// filter's auto-load path.
/// </para>
/// </summary>
internal static class StructuredCallElicitationCoordinator
{
    /// <summary>
    /// Recovers an omitted <c>workspace_load.path</c> before the SDK binder runs. Inspecting the
    /// request shape directly avoids depending on binder exception prose or
    /// <see cref="ArgumentException.ParamName"/>, both of which are SDK implementation details.
    /// </summary>
    internal static async Task<CallToolResult?> TryRecoverMissingWorkspacePathAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var parameters = context.Params;
        if (parameters is null ||
            !string.Equals(
                parameters.Name,
                ElicitationAllowlistPolicy.WorkspaceLoadToolName,
                StringComparison.Ordinal) ||
            !IsParameterMissing(parameters.Arguments, ElicitationAllowlistPolicy.PathParameterName) ||
            !ElicitationAllowlistPolicy.IsElicitationAllowedFor(
                ElicitationAllowlistPolicy.WorkspaceLoadToolName,
                ElicitationAllowlistPolicy.PathParameterName) ||
            !ElicitationChoicePrompt.SupportsElicitation(context))
        {
            return null;
        }

        var elicitRequest = BuildWorkspacePathElicitationRequest(
            ElicitationAllowlistPolicy.WorkspaceLoadToolName,
            ElicitationAllowlistPolicy.PathParameterName);

        var elicitAttempt = await TryRunRecoveryStepAsync(
            () => RequestScopedInputAdapter.RequestElicitationAsResultAsync(
                context,
                RequestScopedInputAdapter.WorkspacePathInputRequestKey,
                elicitRequest,
                logger,
                cancellationToken),
            elicitEx => LogRecoveryFailure(logger, elicitEx, "workspace-path elicitation")).ConfigureAwait(false);
        if (!elicitAttempt.Succeeded)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var elicitResult = elicitAttempt.Value!;

        if (!elicitResult.IsAccepted || elicitResult.Content is null
            || elicitResult.Content.Count != 1
            || !elicitResult.Content.TryGetValue(ElicitationAllowlistPolicy.PathParameterName, out var elicitedValue)
            || elicitedValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(elicitedValue.GetString()))
        {
            logger?.LogInformation("Workspace-path elicitation was declined or returned an invalid form response.");
            return null;
        }

        var existingArgs = parameters.Arguments;
        var newArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (existingArgs is not null)
        {
            foreach (var kvp in existingArgs)
            {
                newArgs[kvp.Key] = kvp.Value;
            }
        }
        newArgs[ElicitationAllowlistPolicy.PathParameterName] = elicitedValue;
        parameters.Arguments = newArgs;

        // Exactly one dispatch follows an accepted input. Validation (including sanctioned-root
        // enforcement) remains owned by workspace_load itself.
        cancellationToken.ThrowIfCancellationRequested();
        var result = await next(context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>
    /// workspaceId-specific recover-load-retry loop: elicits a workspace path, calls
    /// <c>workspace_load</c> via <paramref name="dispatchAsync"/>, extracts the returned
    /// <c>workspaceId</c>, and retries the original tool with that id patched into
    /// <paramref name="originalArguments"/>. A declined/cancelled form result, invalid path,
    /// missing load result id, or ordinary exception from elicitation/load returns
    /// <see langword="null"/> so the caller can surface the existing schema-hint envelope.
    /// Exceptions from the retried original tool propagate to the owning filter so it emits one
    /// classified failure envelope without dispatching the original arguments a second time.
    /// <see cref="OperationCanceledException"/> and the MRTR <see cref="InputRequiredException"/>
    /// control-flow signal also propagate unchanged.
    /// </summary>
    internal static async Task<CallToolResult?> TryRecoverMissingWorkspaceIdAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? originalArguments,
        Func<ElicitRequestParams, ValueTask<ElicitResult>> elicitAsync,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> dispatchAsync,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var elicitRequest = BuildWorkspacePathElicitationRequest(
            toolName,
            ElicitationAllowlistPolicy.WorkspaceIdParameterName);
        var elicitAttempt = await TryRunRecoveryStepAsync(
            () => elicitAsync(elicitRequest),
            elicitEx => LogRecoveryFailure(logger, elicitEx, "workspaceId elicitation")).ConfigureAwait(false);
        if (!elicitAttempt.Succeeded)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var elicitResult = elicitAttempt.Value!;

        if (!elicitResult.IsAccepted || elicitResult.Content is null
            || elicitResult.Content.Count != 1
            || !elicitResult.Content.TryGetValue(ElicitationAllowlistPolicy.PathParameterName, out var pathValue))
        {
            logger?.LogInformation(
                "User declined or cancelled workspaceId recovery elicitation for {Tool}", toolName);
            return null;
        }

        if (pathValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pathValue.GetString()))
        {
            return null;
        }

        var loadArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [ElicitationAllowlistPolicy.PathParameterName] = pathValue,
        };
        var loadAttempt = await TryRunRecoveryStepAsync(
            () => new ValueTask<CallToolResult>(dispatchAsync(
                ElicitationAllowlistPolicy.WorkspaceLoadToolName,
                loadArgs)),
            loadEx => LogRecoveryFailure(logger, loadEx, "workspace_load dispatch")).ConfigureAwait(false);
        if (!loadAttempt.Succeeded)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var loadResult = loadAttempt.Value!;

        var workspaceId = TryExtractWorkspaceId(loadResult);
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            logger?.LogWarning(
                "workspace_load did not return a workspaceId during recovery for {Tool}; falling back to schemaHint envelope.",
                toolName);
            return null;
        }

        var retryArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (originalArguments is not null)
        {
            foreach (var kvp in originalArguments)
            {
                retryArgs[kvp.Key] = kvp.Value;
            }
        }

        retryArgs[ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
            JsonSerializer.SerializeToElement(workspaceId, JsonDefaults.Indented);
        cancellationToken.ThrowIfCancellationRequested();
        var retryResult = await dispatchAsync(toolName, retryArgs).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return retryResult;
    }

    internal static void LogRecoveryFailure(
        ILogger? logger,
        Exception exception,
        string recoveryStep) =>
        logger?.LogWarning(
            "Structured-call recovery step {RecoveryStep} failed with {ExceptionType}; falling back to the existing validation envelope.",
            recoveryStep,
            exception.GetType().Name);

    /// <summary>
    /// Executes one recovery await boundary. Ordinary recovery failures fall through to the
    /// caller's existing envelope; cooperative cancellation always escapes unchanged, and so
    /// does the MRTR input-required protocol signal
    /// (<see cref="InputRequiredException"/>) — swallowing it here would convert
    /// "this call needs client input" into a terminal schema-hint envelope and make the
    /// request-scoped adapter's MRTR leg unreachable.
    /// </summary>
    internal static async ValueTask<(bool Succeeded, T? Value)> TryRunRecoveryStepAsync<T>(
        Func<ValueTask<T>> action,
        Action<Exception> onFailure)
        where T : class
    {
        try
        {
            return (true, await action().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InputRequiredException)
        {
            onFailure(ex);
            return (false, null);
        }
    }

    /// <summary>
    /// Builds the strict path-only elicit request for the
    /// <c>elicit-workspace-path-on-missing-required-arg</c> initiative. Single string field,
    /// required, descriptive prompt naming the tool so the user knows what they're being
    /// asked for. Callers gate this form request through
    /// <see cref="ElicitationChoicePrompt.SupportsElicitation"/>: explicitly URL-only clients are
    /// refused, while form-capable clients and legacy blank elicitation capabilities may proceed.
    /// </summary>
    private static ElicitRequestParams BuildWorkspacePathElicitationRequest(string toolName, string missingParamName)
    {
        var message = string.Equals(
                missingParamName,
                ElicitationAllowlistPolicy.WorkspaceIdParameterName,
                StringComparison.Ordinal)
            ? $"The {toolName} tool was called without a 'workspaceId' argument. " +
              "Provide an absolute path to a .sln, .slnx, or .csproj file; the server will call workspace_load and retry with the recovered workspaceId."
            : $"The {toolName} tool was called without a '{missingParamName}' argument. " +
              "Provide an absolute path to a .sln, .slnx, or .csproj file to continue.";

        return new ElicitRequestParams
        {
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [ElicitationAllowlistPolicy.PathParameterName] = new ElicitRequestParams.StringSchema
                    {
                        Title = "Workspace path",
                        Description =
                            "Absolute path to a .sln, .slnx, or .csproj file on the local filesystem.",
                    },
                },
                Required = [ElicitationAllowlistPolicy.PathParameterName],
            },
        };
    }

    /// <summary>
    /// Temporarily swaps <paramref name="context"/>'s arguments, invokes the handler already bound
    /// to the original tool through <paramref name="next"/>, then restores the originals in a
    /// <c>finally</c>. The filter resolves registered tool primitives separately for cross-tool
    /// dispatch; changing the name here does not reroute a bound handler.
    /// </summary>
    internal static async Task<CallToolResult> DispatchWithTemporaryArgumentsAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string toolName,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken)
    {
        var originalToolName = context.Params!.Name;
        var originalArgs = context.Params.Arguments;
        try
        {
            context.Params.Name = toolName;
            context.Params.Arguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            return await next(context, cancellationToken).ConfigureAwait(false);
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
            context.Params.Arguments = originalArgs;
        }
    }

    internal static bool IsParameterMissing(
        IDictionary<string, JsonElement>? arguments,
        string parameterName) =>
        arguments is null ||
        !arguments.TryGetValue(parameterName, out var value) ||
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    /// <summary>
    /// Extracts the <c>workspaceId</c> string from a <c>workspace_load</c> result's JSON body,
    /// or <see langword="null"/> when the content is empty, non-text, non-JSON, or lacks the
    /// property. <c>internal</c> so the filter's retained auto-load path can reuse it.
    /// </summary>
    internal static string? TryExtractWorkspaceId(CallToolResult result)
    {
        if (result.Content is null || result.Content.Count == 0) return null;
        if (result.Content[0] is not TextContentBlock text || string.IsNullOrWhiteSpace(text.Text)) return null;

        try
        {
            using var doc = JsonDocument.Parse(text.Text);
            return doc.RootElement.TryGetProperty(ElicitationAllowlistPolicy.WorkspaceIdParameterName, out var id)
                   && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
