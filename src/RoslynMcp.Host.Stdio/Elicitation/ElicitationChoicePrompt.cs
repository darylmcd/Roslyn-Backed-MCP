using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;

namespace RoslynMcp.Host.Stdio.Elicitation;

/// <summary>
/// The shared elicitation-choice contract, extracted to its own namespace so
/// <c>RoslynMcp.Host.Stdio.Middleware</c> and <c>RoslynMcp.Host.Stdio.Tools</c> can both depend on
/// it instead of on each other. Owns the two members that were the sole reason
/// <c>Tools</c> imported <c>Middleware</c>: the form-capability predicate
/// (<see cref="HasElicitation"/>) and the select-from-N choice prompt
/// (<see cref="TryElicitChoiceAsync"/>).
///
/// <para>
/// <b>Layering invariant:</b> this type must never import <c>RoslynMcp.Host.Stdio.Middleware</c>
/// or <c>RoslynMcp.Host.Stdio.Tools</c>. It may depend on the neutral protocol-compatibility
/// boundary. Importing either consumer layer re-creates the namespace cycle this type exists to
/// break and is caught by the namespace-cycle guard test in
/// <c>tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.RepoSolutionAnalysis.cs</c> (the guard
/// is named by file rather than by method so a test rename cannot silently rot this reference —
/// elicitation-forwarder-collapse-trychoice-docs).
/// </para>
/// </summary>
internal static class ElicitationChoicePrompt
{
    internal const int MaxOptions = 20;

    /// <summary>
    /// Returns <see langword="true"/> when the resolved client capability can satisfy the
    /// in-band form requests emitted by this component. Explicit form support qualifies. A blank
    /// legacy elicitation object also qualifies because the SDK historically normalized that
    /// shape to form support; an explicit URL-only capability does not.
    /// </summary>
    /// <param name="capabilities">An already-resolved client-capability snapshot.</param>
    /// <returns>
    /// <see langword="true"/> for explicit form support or the legacy blank-object form; otherwise
    /// <see langword="false"/>, including null capabilities and URL-only elicitation support.
    /// </returns>
    public static bool HasElicitation(ClientCapabilities? capabilities)
    {
        var elicitation = capabilities?.Elicitation;
        // The SDK normalizes the legacy blank object to form support on the wire. Preserve that
        // compatibility for directly constructed capabilities, but reject URL-only clients: every
        // workflow owned here emits an in-band form request.
        return elicitation is not null &&
            (elicitation.Form is not null || elicitation.Url is null);
    }

    /// <summary>
    /// Request-aware form-capability predicate. Modern SEP-2575 requests use only their
    /// authoritative per-request metadata; legacy initialize-handshake requests use the server
    /// snapshot and retain the blank-object compatibility described by <see cref="HasElicitation"/>.
    /// </summary>
    /// <param name="context">The current tool-call context.</param>
    /// <returns>
    /// <see langword="true"/> when the request's authoritative capability source supports the
    /// form workflow; otherwise <see langword="false"/>, including a null context or URL-only client.
    /// </returns>
    public static bool SupportsElicitation(RequestContext<CallToolRequestParams>? context) =>
        context is not null &&
        HasElicitation(RequestProtocolFeatureGate.ResolveClientCapabilities(context));

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: shared select-from-N elicitation
    /// helper. Builds an enum-shaped request-scoped form whose options carry short candidate
    /// labels and stable string keys, then returns the chosen key (see <c>returns</c> for the exact
    /// refusal arms). The caller maps that key back to the original candidate and returns or
    /// continues with it. This is the sole definition of the choice prompt; consumer layers do
    /// not define forwarding delegates.
    /// </summary>
    /// <param name="context">
    /// The current tool-call context; <see langword="null"/> returns <see langword="null"/>.
    /// </param>
    /// <param name="paramName">
    /// Name of the schema field carrying the picked option — also the dictionary key the
    /// SDK populates in <see cref="ElicitResult.Content"/>. Conventionally <c>"choice"</c>.
    /// </param>
    /// <param name="title">Short title shown above the option list.</param>
    /// <param name="description">
    /// Longer description explaining why the operator is being asked to choose.
    /// </param>
    /// <param name="options">
    /// The select-from-N options. <c>Key</c> is the stable identifier returned to the caller,
    /// <c>Label</c> is the human-readable text shown in the form prompt.
    /// </param>
    /// <param name="cancellationToken">Cancellation token (request-scoped).</param>
    /// <returns>
    /// The chosen <c>Key</c> when the user accepts. <see langword="null"/> when:
    /// <paramref name="context"/> is <see langword="null"/>; the authoritative capability is
    /// absent or explicitly URL-only (a legacy blank object remains form-compatible); the request
    /// is malformed
    /// (<paramref name="paramName"/> empty, <paramref name="options"/> null/empty/over
    /// <see cref="MaxOptions"/>, or any option key empty or duplicated); the user
    /// declined/cancelled or the adapter returned no usable result; accepted content does not
    /// contain exactly the one requested field; the chosen value is not a string or is not one of
    /// the offered keys; or the SDK threw
    /// <see cref="InvalidOperationException"/> or <see cref="McpException"/> (logged at Debug, then
    /// degraded to the additive-list fallback).
    ///
    /// <para>
    /// <b>Not</b> null on cooperative cancellation or MRTR hand-off:
    /// <see cref="OperationCanceledException"/> and <c>InputRequiredException</c> propagate to the
    /// caller. Other unexpected exception types propagate as well; only the two documented SDK
    /// failures above degrade to the additive-list fallback.
    /// </para>
    /// </returns>
    public static async Task<string?> TryElicitChoiceAsync(
        RequestContext<CallToolRequestParams>? context,
        string paramName,
        string title,
        string description,
        IReadOnlyList<(string Key, string Label)> options,
        CancellationToken cancellationToken)
    {
        if (context is null) return null;
        if (!SupportsElicitation(context)) return null;

        return await TryElicitChoiceCoreAsync(
            paramName,
            title,
            description,
            options,
            (request, token) => RequestScopedInputAdapter.RequestElicitationAsync(
                context,
                RequestScopedInputAdapter.SymbolChoiceInputRequestKey,
                request,
                GetLogger(context),
                token),
            ex => LogElicitFailure(context, ex),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string?> TryElicitChoiceCoreAsync(
        string paramName,
        string title,
        string description,
        IReadOnlyList<(string Key, string Label)> options,
        Func<ElicitRequestParams, CancellationToken,
            ValueTask<(RequestScopedInputOutcome Outcome, ElicitResult? Result)>> requestAsync,
        Action<Exception>? onExpectedFailure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestAsync);
        if (string.IsNullOrEmpty(paramName) ||
            options is null ||
            options.Count == 0 ||
            options.Count > MaxOptions)
        {
            return null;
        }

        var oneOf = new List<ElicitRequestParams.EnumSchemaOption>(options.Count);
        var allowedKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var (key, label) = options[i];
            if (string.IsNullOrEmpty(key) || !allowedKeys.Add(key)) return null;
            oneOf.Add(new ElicitRequestParams.EnumSchemaOption
            {
                Const = key,
                Title = string.IsNullOrEmpty(label) ? key : label,
            });
        }
        if (oneOf.Count == 0) return null;

        var request = new ElicitRequestParams
        {
            Message = description,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [paramName] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                    {
                        Title = title,
                        Description = description,
                        OneOf = oneOf,
                    },
                },
                Required = [paramName],
            },
        };

        ElicitResult? result;
        try
        {
            var (_, inputResult) = await requestAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            result = inputResult;
        }
        catch (InvalidOperationException ex)
        {
            // ModelContextProtocol.Core 2.1's ElicitAsync XML contract declares only
            // InvalidOperationException (unsupported) and McpException (request/client error)
            // beyond caller-precondition failures. IOException/ObjectDisposedException are not
            // declared transport outcomes and intentionally remain hard failures. Keep this
            // list explicit so cancellation and unexpected SDK regressions propagate.
            onExpectedFailure?.Invoke(ex);
            return null;
        }
        catch (McpException ex)
        {
            // Client-side error response to the elicitation request. Same fallback as above.
            onExpectedFailure?.Invoke(ex);
            return null;
        }

        if (result is null || !result.IsAccepted || result.Content is null || result.Content.Count != 1)
        {
            return null;
        }
        if (!result.Content.TryGetValue(paramName, out var chosen)) return null;
        var chosenKey = chosen.ValueKind == JsonValueKind.String ? chosen.GetString() : null;
        return chosenKey is not null && allowedKeys.Contains(chosenKey) ? chosenKey : null;
    }

    /// <summary>
    /// Logs the swallowed <see cref="InvalidOperationException"/> / <see cref="McpException"/>
    /// at Debug so the additive-list fallback is discoverable during troubleshooting instead of
    /// disappearing silently (elicitation-trychoice-cancellation-swallow). Resolves the logger
    /// from the request context's service provider and no-ops when no
    /// <see cref="ILoggerFactory"/> is registered.
    /// </summary>
    private static ILogger? GetLogger(RequestContext<CallToolRequestParams> context) =>
        context.Services?.GetService<ILoggerFactory>()?
            .CreateLogger(typeof(ElicitationChoicePrompt).FullName ?? nameof(ElicitationChoicePrompt));

    private static void LogElicitFailure(RequestContext<CallToolRequestParams> context, Exception ex) =>
        LogExpectedFailure(GetLogger(context), ex);

    internal static void LogExpectedFailure(ILogger? logger, Exception ex) =>
        logger?.LogDebug(
            "TryElicitChoiceAsync: elicitation request failed with {ExceptionType}; falling back to the additive list response.",
            ex.GetType().Name);
}
