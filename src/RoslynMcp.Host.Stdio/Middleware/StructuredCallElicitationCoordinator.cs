using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Elicitation;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The elicitation/retry orchestration layer extracted from <see cref="StructuredCallToolFilter"/>.
/// Owns the <em>how</em> of asking the user for a missing value via MCP <c>elicitation/create</c>
/// and re-dispatching the original call — the exception-path recovery
/// (<see cref="TryElicitAndRetryAsync"/>) and the workspaceId-specific recover-load-retry loop
/// (<see cref="TryRecoverMissingWorkspaceIdAsync"/>). Whether a parameter <em>may</em> be elicited
/// stays in <see cref="ElicitationAllowlistPolicy"/>; metrics stay in
/// <see cref="CallMetricsRecorder"/>; the pre-dispatch auto-resolve/auto-load flow and
/// error-envelope construction stay in the filter. The shared select-from-N picker is NOT owned
/// here — its canonical (and only) home is
/// <see cref="ElicitationChoicePrompt.TryElicitChoiceAsync"/>, in the cycle-free
/// <c>RoslynMcp.Host.Stdio.Elicitation</c> namespace so <c>Tools</c> can call it without importing
/// <c>Middleware</c>.
///
/// <para>
/// <see cref="StructuredCallToolFilter"/> keeps thin delegates forwarding to
/// <see cref="TryElicitAndRetryAsync"/> and <see cref="TryRecoverMissingWorkspaceIdAsync"/>, so its
/// historical static call surface (consumed by the existing filter test suites) is preserved.
/// <see cref="DispatchWithTemporaryArgumentsAsync"/> and <see cref="TryExtractWorkspaceId"/> are
/// <c>internal</c> (not <c>private</c>) so the filter's retained <c>TryAutoLoadWorkspaceAsync</c>
/// can keep calling them directly.
/// </para>
/// </summary>
internal static class StructuredCallElicitationCoordinator
{
    // Duplicated from ElicitationAllowlistPolicy / StructuredCallToolFilter (a private const does
    // not cross the class boundary). Retained here because the recover/load/retry body references
    // all three (workspace_load dispatch, workspaceId patching, path elicit).
    private const string WorkspaceLoadToolName = "workspace_load";
    private const string WorkspaceIdParameterName = "workspaceId";
    private const string PathParameterName = "path";

    /// <summary>
    /// Core of the elicitation-fallback path: when <paramref name="ex"/> is an
    /// <c>InvalidArgument</c> for a missing required parameter on a tool whose
    /// (toolName, paramName) pair is on the elicitation allowlist AND the client supports
    /// elicitation, this method asks the user for the missing value via
    /// <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>, patches
    /// the elicited value into the request's <c>Arguments</c> dictionary, and re-invokes
    /// <paramref name="next"/>. Returns <see langword="null"/> when the recovery does not
    /// apply (any layer of the gate fails) — the caller falls through to
    /// <see cref="StructuredCallToolFilter.BuildErrorResult"/> with the existing schema-hint envelope.
    /// </summary>
    /// <remarks>
    /// We do NOT hold any workspace lock or other resource during the elicit wait — that
    /// wait is unbounded (user-paced) and would otherwise starve concurrent tool calls.
    /// The original failure already released anything <c>next</c> had taken; the retry
    /// re-acquires from a clean state.
    /// </remarks>
    internal static async Task<CallToolResult?> TryElicitAndRetryAsync(
        RequestContext<CallToolRequestParams> context,
        Exception ex,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // Layer 0: only InvalidArgument-like exceptions with a known param name qualify.
        if (!TryGetMissingParam(ex, out var missingParam) || string.IsNullOrEmpty(missingParam))
        {
            return null;
        }

        var toolName = context.Params?.Name;
        if (string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        // Layer 1 + 2: allowlist + sensitive-name refusal in one call. Either failure
        // means "do not elicit" — the legacy schemaHint envelope is the right fallback.
        if (!ElicitationAllowlistPolicy.IsElicitationAllowedFor(toolName, missingParam))
        {
            // Defense-in-depth log: explicit "refused to elicit sensitive field" branch
            // so a future audit can grep for it. Not an error — we refuse silently and
            // fall through to the normal envelope.
            if (ElicitationAllowlistPolicy.IsSensitiveFieldName(missingParam))
            {
                logger?.LogWarning(
                    "Refusing to elicit sensitive parameter '{Param}' on tool '{Tool}'. " +
                    "MCP spec § Elicitation security: 'Servers MUST NOT request sensitive information'.",
                    missingParam, toolName);
            }
            return null;
        }

        // Layer 3: client must support elicitation. server.ClientCapabilities.Elicitation
        // is established at initialize-handshake time; this is a property read, not an RPC.
        if (!ElicitationChoicePrompt.HasElicitation(context.Server?.ClientCapabilities))
        {
            return null;
        }

        if (ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor(toolName, missingParam))
        {
            return await TryRecoverMissingWorkspaceIdAsync(
                toolName,
                CopyArguments(context.Params!.Arguments),
                request => context.Server!.ElicitAsync(request, cancellationToken),
                (dispatchToolName, arguments) =>
                    DispatchWithTemporaryArgumentsAsync(context, next, dispatchToolName, arguments, cancellationToken),
                logger,
                cancellationToken).ConfigureAwait(false);
        }

        // Build the strict elicit request. Single string field ('path'), required, with a
        // descriptive prompt — the user sees a one-field form (in form mode) or navigates
        // to the URL (in url mode); the client picks based on its declared sub-capability.
        var elicitRequest = BuildWorkspacePathElicitationRequest(toolName, missingParam);

        var elicitAttempt = await TryRunRecoveryStepAsync(
            () => context.Server!.ElicitAsync(elicitRequest, cancellationToken),
            elicitEx => logger?.LogWarning(elicitEx,
                "Elicitation request failed for {Tool}.{Param}; falling back to schemaHint envelope.",
                toolName, missingParam)).ConfigureAwait(false);
        if (!elicitAttempt.Succeeded)
        {
            return null;
        }

        var elicitResult = elicitAttempt.Value!;

        // User declined or cancelled — surface the original error. Don't retry with empty
        // input; that would just re-trigger the same InvalidArgument.
        if (!elicitResult.IsAccepted || elicitResult.Content is null
            || !elicitResult.Content.TryGetValue(missingParam, out var elicitedValue))
        {
            logger?.LogInformation(
                "User declined or cancelled elicitation for {Tool}.{Param}", toolName, missingParam);
            return null;
        }

        // Patch the missing parameter into the arguments dictionary and retry.
        // CallToolRequestParams.Arguments is IReadOnlyDictionary<string, JsonElement>?;
        // we materialize a new mutable copy, set the elicited value, and assign it back.
        var existingArgs = context.Params!.Arguments;
        var newArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (existingArgs is not null)
        {
            foreach (var kvp in existingArgs)
            {
                newArgs[kvp.Key] = kvp.Value;
            }
        }
        newArgs[missingParam] = elicitedValue;
        context.Params.Arguments = newArgs;

        // Re-dispatch. If this throws too, the exception bubbles back to the outer catch
        // — we don't loop indefinitely.
        return await next(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// workspaceId-specific recover-load-retry loop: elicits a workspace path, calls
    /// <c>workspace_load</c> via <paramref name="dispatchAsync"/>, extracts the returned
    /// <c>workspaceId</c>, and retries the original tool with that id patched into
    /// <paramref name="originalArguments"/>. Every failure mode (elicit throws, user declines,
    /// non-string path, load dispatch throws, load returns no id, retry throws) falls through to
    /// <see langword="null"/> so the caller can surface the existing schema-hint envelope.
    /// </summary>
    internal static async Task<CallToolResult?> TryRecoverMissingWorkspaceIdAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? originalArguments,
        Func<ElicitRequestParams, ValueTask<ElicitResult>> elicitAsync,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> dispatchAsync,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var elicitRequest = BuildWorkspacePathElicitationRequest(toolName, WorkspaceIdParameterName);
        var elicitAttempt = await TryRunRecoveryStepAsync(
            () => elicitAsync(elicitRequest),
            elicitEx => logger?.LogWarning(elicitEx,
                "WorkspaceId recovery elicitation failed for {Tool}; falling back to schemaHint envelope.",
                toolName)).ConfigureAwait(false);
        if (!elicitAttempt.Succeeded)
        {
            return null;
        }

        var elicitResult = elicitAttempt.Value!;

        if (!elicitResult.IsAccepted || elicitResult.Content is null
            || !elicitResult.Content.TryGetValue(PathParameterName, out var pathValue))
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
            [PathParameterName] = pathValue,
        };
        var loadAttempt = await TryRunRecoveryStepAsync(
            () => new ValueTask<CallToolResult>(dispatchAsync(WorkspaceLoadToolName, loadArgs)),
            loadEx => logger?.LogWarning(loadEx,
                "workspace_load dispatch threw during workspaceId recovery for {Tool}; falling back to schemaHint envelope.",
                toolName)).ConfigureAwait(false);
        if (!loadAttempt.Succeeded)
        {
            return null;
        }

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

        retryArgs[WorkspaceIdParameterName] = JsonSerializer.SerializeToElement(workspaceId, JsonDefaults.Indented);
        var retryAttempt = await TryRunRecoveryStepAsync(
            () => new ValueTask<CallToolResult>(dispatchAsync(toolName, retryArgs)),
            retryEx => logger?.LogWarning(retryEx,
                "Retried tool dispatch threw during workspaceId recovery for {Tool}; falling back to schemaHint envelope.",
                toolName)).ConfigureAwait(false);
        return retryAttempt.Succeeded ? retryAttempt.Value : null;
    }

    /// <summary>
    /// Executes one recovery await boundary. Ordinary recovery failures fall through to the
    /// caller's existing envelope; cooperative cancellation always escapes unchanged.
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onFailure(ex);
            return (false, null);
        }
    }

    /// <summary>
    /// Inspects <paramref name="ex"/> for the InvalidArgument-shaped binding-failure
    /// exceptions the SDK delivers when a required parameter is missing. Returns
    /// <see langword="true"/> with the parameter name on success, otherwise
    /// <see langword="false"/> (caller skips the elicit path).
    /// </summary>
    private static bool TryGetMissingParam(Exception ex, out string? paramName)
    {
        // The SDK delivers two shapes (see ToolErrorHandler.ClassifyError comments):
        //   1. Direct ArgumentException / ArgumentNullException carrying ParamName.
        //   2. Wrapped in TargetInvocationException or InvalidOperationException whose
        //      InnerException is the real ArgumentException.
        if (ex is ArgumentException directArg && !string.IsNullOrEmpty(directArg.ParamName))
        {
            paramName = directArg.ParamName;
            return true;
        }
        if (ex.InnerException is ArgumentException innerArg && !string.IsNullOrEmpty(innerArg.ParamName))
        {
            paramName = innerArg.ParamName;
            return true;
        }
        paramName = null;
        return false;
    }

    /// <summary>
    /// Builds the strict path-only elicit request for the
    /// <c>elicit-workspace-path-on-missing-required-arg</c> initiative. Single string field,
    /// required, descriptive prompt naming the tool so the user knows what they're being
    /// asked for. Form mode is the canonical shape; clients that only support url mode can
    /// still complete via out-of-band navigation.
    /// </summary>
    private static ElicitRequestParams BuildWorkspacePathElicitationRequest(string toolName, string missingParamName)
    {
        var message = string.Equals(missingParamName, WorkspaceIdParameterName, StringComparison.Ordinal)
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
                    [PathParameterName] = new ElicitRequestParams.StringSchema
                    {
                        Title = "Workspace path",
                        Description =
                            "Absolute path to a .sln, .slnx, or .csproj file on the local filesystem.",
                    },
                },
                Required = [PathParameterName],
            },
        };
    }

    /// <summary>
    /// Temporarily swaps <paramref name="context"/>'s tool name and arguments, dispatches through
    /// <paramref name="next"/>, then restores the originals in a <c>finally</c>. Used to run
    /// <c>workspace_load</c> (or a patched retry) on the same request context without leaking the
    /// mutation to the caller. <c>internal</c> so the filter's retained auto-load path can reuse it.
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
        finally
        {
            context.Params.Name = originalToolName;
            context.Params.Arguments = originalArgs;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement>? CopyArguments(
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return null;
        return new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
    }

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
            return doc.RootElement.TryGetProperty(WorkspaceIdParameterName, out var id)
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
