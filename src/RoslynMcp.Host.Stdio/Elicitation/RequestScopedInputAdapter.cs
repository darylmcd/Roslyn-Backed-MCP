using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Elicitation;

/// <summary>
/// Sanitized classification of one request-scoped input exchange. The caller maps everything
/// except <see cref="Accepted"/> to its existing <see langword="null"/> → schema-hint-envelope
/// fallback; no raw exception text or client payload ever rides on the outcome.
/// </summary>
internal enum RequestScopedInputOutcome
{
    /// <summary>The client accepted and returned form content.</summary>
    Accepted,

    /// <summary>The client declined or cancelled the input request.</summary>
    DeclinedOrCancelled,

    /// <summary>
    /// The retry request carried an input response that could not be deserialized into an
    /// <see cref="ElicitResult"/>. Sanitized: the malformed payload is never echoed back.
    /// </summary>
    MalformedResponse,
}

/// <summary>
/// The single request-scoped boundary through which the <c>tools/call</c> recovery pipeline asks
/// the user for input, portable across both SDK 2.1 session modes (SEP-2322 MRTR on the
/// <c>2026-07-28</c> revision, and the <c>2025-11-25</c>-and-earlier initialize-handshake
/// sessions).
///
/// <para><b>Why direct <see cref="McpServer.ElicitAsync"/> is stateful-only while MRTR is
/// portable:</b> <c>ElicitAsync</c> sends a nested <c>elicitation/create</c> JSON-RPC request to
/// the client and suspends the in-flight <c>tools/call</c> handler until the response arrives.
/// That nested-continuation shape requires a stateful session — the same server instance must
/// stay alive (and the same transport channel open) between the outbound request and the inbound
/// response. On a stateless session (per-request HTTP under the 2026-07-28 revision) there is no
/// such continuity, so the direct call has no portable equivalent. MRTR inverts the flow: the
/// server terminates the initial <c>tools/call</c> with an <see cref="InputRequiredResult"/>
/// (via <see cref="InputRequiredException"/>), the client resolves the embedded
/// <see cref="InputRequest"/>s locally, and the client RETRIES the original request carrying
/// <see cref="RequestParams.InputResponses"/> — every round trip is a self-contained request, so
/// the flow works identically on stateful and stateless sessions.</para>
///
/// <para><b>Request-scoped only:</b> the retry leg reads exclusively
/// <c>context.Params.InputResponses</c> — the responses the client attached to THIS request.
/// There is no session or static cache of capabilities or responses; per SEP-2575 the server
/// must not infer per-request client state from previous requests.</para>
///
/// <para><b>Policy-free:</b> the adapter takes a caller-built <see cref="ElicitRequestParams"/>
/// and owns only the transport-era decision. Which parameters may be elicited
/// (<c>ElicitationAllowlistPolicy</c>) and what the form asks for (the coordinator's
/// workspace-path schema) stay with the callers.</para>
///
/// <para><b>Cancellation contract:</b> the adapter adds no <c>catch</c> around the legacy
/// <see cref="McpServer.ElicitAsync"/> leg — <see cref="OperationCanceledException"/> from any
/// leg propagates unchanged so the filter's cooperative-cancellation rethrow stays intact
/// (mirroring the <c>when (ex is not OperationCanceledException)</c> guard in
/// <c>StructuredCallElicitationCoordinator.TryRunRecoveryStepAsync</c>). The only guarded spot
/// is the retry-leg deserialization, whose failure is classified as
/// <see cref="RequestScopedInputOutcome.MalformedResponse"/> instead of escaping as an internal
/// error.</para>
/// </summary>
internal static class RequestScopedInputAdapter
{
    /// <summary>
    /// The server-assigned identifier under which the elicitation <see cref="InputRequest"/> is
    /// published in <see cref="InputRequiredResult.InputRequests"/>, and under which the retry
    /// request's <see cref="RequestParams.InputResponses"/> is consumed. Deterministic so the
    /// initial and retry legs of a stateless round trip agree without any server-side state.
    /// </summary>
    internal const string ElicitationInputRequestKey = "roslynmcp.elicitation";

    /// <summary>
    /// Requests user input for <paramref name="request"/> through the era-appropriate mechanism:
    /// <list type="number">
    ///   <item><b>Retry leg</b> — when the current request carries an input response keyed
    ///   <see cref="ElicitationInputRequestKey"/>, consume it (request-scoped only) and classify
    ///   the outcome without any client round trip.</item>
    ///   <item><b>MRTR leg</b> — when the client supports MRTR
    ///   (<see cref="McpServer.IsMrtrSupported"/>), throw <see cref="InputRequiredException"/>
    ///   so the SDK emits an <see cref="InputRequiredResult"/>; the client retries with the
    ///   response attached and re-enters via the retry leg.</item>
    ///   <item><b>Stateful legacy leg</b> — otherwise, fall back to the direct nested
    ///   <see cref="McpServer.ElicitAsync"/> continuation (initialize-handshake sessions).</item>
    /// </list>
    /// </summary>
    /// <exception cref="InputRequiredException">
    /// Thrown on the MRTR leg. This is a protocol signal, not a failure — it must reach the SDK
    /// unswallowed (see the <c>StructuredCallToolFilter</c> rethrow and the
    /// <c>TryRunRecoveryStepAsync</c> exception filter).
    /// </exception>
    internal static async ValueTask<(RequestScopedInputOutcome Outcome, ElicitResult? Result)> RequestInputAsync(
        RequestContext<CallToolRequestParams> context,
        ElicitRequestParams request,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        // Retry leg: the client already answered on a previous round trip of THIS logical
        // request. Consume only this request's own inputResponses — no session/static cache.
        if (context.Params?.InputResponses is { } inputResponses &&
            inputResponses.TryGetValue(ElicitationInputRequestKey, out var inputResponse))
        {
            return ClassifyInputResponse(inputResponse, logger);
        }

        if (context.Server?.IsMrtrSupported is true)
        {
            // MRTR leg: terminate this round trip with an input-required protocol signal.
            throw new InputRequiredException(
                new Dictionary<string, InputRequest>(StringComparer.Ordinal)
                {
                    [ElicitationInputRequestKey] = InputRequest.ForElicitation(request),
                },
                requestState: null);
        }

        // Stateful legacy leg: nested elicitation/create continuation. No catch here —
        // OperationCanceledException must propagate unchanged, and ordinary SDK failures are
        // owned by the caller's guarded recovery step.
        var elicitResult = await context.Server!.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
        return elicitResult.IsAccepted
            ? (RequestScopedInputOutcome.Accepted, elicitResult)
            : (RequestScopedInputOutcome.DeclinedOrCancelled, elicitResult);
    }

    /// <summary>
    /// Delegate-shaped projection of <see cref="RequestInputAsync"/> matching the coordinator's
    /// existing <c>Func&lt;ElicitRequestParams, ValueTask&lt;ElicitResult&gt;&gt;</c> seam.
    /// Non-accept outcomes surface as a non-accepted <see cref="ElicitResult"/> so the caller's
    /// decline handling (fall through to the schema-hint envelope) applies uniformly; a
    /// malformed response maps to a sanitized synthetic cancel result carrying none of the raw
    /// payload.
    /// </summary>
    internal static async ValueTask<ElicitResult> RequestInputAsElicitResultAsync(
        RequestContext<CallToolRequestParams> context,
        ElicitRequestParams request,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var (_, result) = await RequestInputAsync(context, request, logger, cancellationToken).ConfigureAwait(false);
        return result ?? new ElicitResult { Action = "cancel" };
    }

    private static (RequestScopedInputOutcome Outcome, ElicitResult? Result) ClassifyInputResponse(
        InputResponse inputResponse,
        ILogger? logger)
    {
        ElicitResult? result;
        try
        {
            result = inputResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        }
        catch (JsonException)
        {
            // Narrow, deliberate: a malformed client payload is a classified outcome, not an
            // internal error. The payload itself is never logged or echoed (redaction contract).
            result = null;
        }

        if (result is null)
        {
            logger?.LogWarning(
                "Retry request carried an input response under '{Key}' that did not deserialize " +
                "to an ElicitResult; classifying as malformed.", ElicitationInputRequestKey);
            return (RequestScopedInputOutcome.MalformedResponse, null);
        }

        return result.IsAccepted
            ? (RequestScopedInputOutcome.Accepted, result)
            : (RequestScopedInputOutcome.DeclinedOrCancelled, result);
    }
}
