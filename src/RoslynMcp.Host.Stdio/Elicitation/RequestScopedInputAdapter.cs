using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;

namespace RoslynMcp.Host.Stdio.Elicitation;

/// <summary>
/// Sanitized classification of one request-scoped input exchange. Each caller maps non-accepted
/// outcomes to its workflow-specific deterministic fallback; no raw exception text or client
/// payload ever rides on the outcome.
/// </summary>
internal enum RequestScopedInputOutcome
{
    /// <summary>The client accepted and returned a usable workflow-specific result.</summary>
    Accepted,

    /// <summary>The client declined or cancelled the input request.</summary>
    DeclinedOrCancelled,

    /// <summary>
    /// The retry request carried an input response that could not be deserialized or validated as
    /// the workflow's expected result. Sanitized: the malformed payload is never echoed back.
    /// </summary>
    MalformedResponse,

    /// <summary>The current request/client cannot perform this request-scoped exchange.</summary>
    Unsupported,
}

/// <summary>
/// The single request-scoped boundary through which the <c>tools/call</c> recovery pipeline asks
/// the user for input, portable across both supported SDK session modes (SEP-2322 MRTR on the
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
    /// Stable workflow-specific identifiers under which <see cref="InputRequest"/> values are
    /// published and retry responses are consumed. Distinct keys prevent one logical call's
    /// workspace, symbol, and sampling exchanges from consuming each other's responses.
    /// </summary>
    internal const string WorkspacePathInputRequestKey = "roslynmcp.workspace-path";
    internal const string SymbolChoiceInputRequestKey = "roslynmcp.symbol-choice";
    internal const string SamplingInputRequestKey = "roslynmcp.sampling";

    /// <summary>
    /// Requests user input for <paramref name="request"/> through the era-appropriate mechanism:
    /// <list type="number">
    ///   <item><b>MRTR retry leg</b> — on a 2026-07-28 request carrying an input response keyed
    ///   <paramref name="inputRequestKey"/>, consume it (request-scoped only) and classify the
    ///   outcome without any client round trip. Legacy requests ignore this newer-protocol field.</item>
    ///   <item><b>MRTR leg</b> — when the negotiated request protocol supports MRTR
    ///   (<see cref="RequestProtocolFeatureGate.SupportsJuly2026Features{TParams}"/>), throw
    ///   <see cref="InputRequiredException"/>
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
    internal static async ValueTask<(RequestScopedInputOutcome Outcome, ElicitResult? Result)> RequestElicitationAsync(
        RequestContext<CallToolRequestParams> context,
        string inputRequestKey,
        ElicitRequestParams request,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRequestKey);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var supportsMrtr = RequestProtocolFeatureGate.SupportsJuly2026Features(context);

        // MRTR retry leg: the client already answered on a previous round trip of THIS logical
        // request. Consume only this request's own inputResponses — no session/static cache.
        // Legacy requests must not bypass their nested elicitation/create continuation by
        // hand-crafting a newer-protocol inputResponses member.
        if (supportsMrtr &&
            context.Params?.InputResponses is { } inputResponses &&
            inputResponses.TryGetValue(inputRequestKey, out var inputResponse))
        {
            var classified = ClassifyElicitationResponse(inputRequestKey, inputResponse, logger);
            cancellationToken.ThrowIfCancellationRequested();
            return classified;
        }

        if (supportsMrtr)
        {
            // MRTR leg: terminate this round trip with an input-required protocol signal.
            throw new InputRequiredException(
                new Dictionary<string, InputRequest>(StringComparer.Ordinal)
                {
                    [inputRequestKey] = InputRequest.ForElicitation(request),
                },
                requestState: null);
        }

        // Stateful legacy leg: nested elicitation/create continuation. No catch here —
        // OperationCanceledException must propagate unchanged, and ordinary SDK failures are
        // owned by the caller's guarded recovery step.
        var elicitResult = await context.Server!.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return elicitResult.IsAccepted
            ? (RequestScopedInputOutcome.Accepted, elicitResult)
            : (RequestScopedInputOutcome.DeclinedOrCancelled, elicitResult);
    }

    /// <summary>
    /// Delegate-shaped projection of <see cref="RequestElicitationAsync"/> matching the coordinator's
    /// existing <c>Func&lt;ElicitRequestParams, ValueTask&lt;ElicitResult&gt;&gt;</c> seam.
    /// Non-accept outcomes surface as a non-accepted <see cref="ElicitResult"/> so the caller's
    /// decline handling (fall through to the schema-hint envelope) applies uniformly; a
    /// malformed response maps to a sanitized synthetic cancel result carrying none of the raw
    /// payload.
    /// </summary>
    internal static async ValueTask<ElicitResult> RequestElicitationAsResultAsync(
        RequestContext<CallToolRequestParams> context,
        string inputRequestKey,
        ElicitRequestParams request,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var (_, result) = await RequestElicitationAsync(
            context,
            inputRequestKey,
            request,
            logger,
            cancellationToken).ConfigureAwait(false);
        return result ?? new ElicitResult { Action = "cancel" };
    }

    /// <summary>
    /// Requests a sampling completion through MRTR. Sampling has no legacy nested-request leg:
    /// pre-MRTR or sampling-incapable clients receive <see cref="RequestScopedInputOutcome.Unsupported"/>
    /// so callers retain deterministic behavior without relying on the deprecated SDK API.
    /// </summary>
#pragma warning disable MCP9005 // The SDK marks sampling itself obsolete; this adapter is the bounded MRTR compatibility boundary.
    internal static (RequestScopedInputOutcome Outcome, string? Text) RequestSampling(
        RequestContext<CallToolRequestParams> context,
        string promptText,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptText);

        // Capability and protocol support gate the entire exchange, including retries. A client
        // must not be able to bypass the sampling boundary by hand-crafting inputResponses on a
        // legacy request or on a modern request that did not advertise sampling.
        if (!RequestProtocolFeatureGate.SupportsJuly2026Features(context) ||
            RequestProtocolFeatureGate.ResolveClientCapabilities(context)?.Sampling is null)
        {
            return (RequestScopedInputOutcome.Unsupported, null);
        }

        if (context.Params?.InputResponses is { } inputResponses &&
            inputResponses.TryGetValue(SamplingInputRequestKey, out var inputResponse))
        {
            var (outcome, result) = ClassifySamplingResponse(inputResponse, logger);
            var text = result?.Content?
                .OfType<TextContentBlock>()
                .Select(static block => block.Text)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            return (outcome, text);
        }

        throw new InputRequiredException(
            new Dictionary<string, InputRequest>(StringComparer.Ordinal)
            {
                [SamplingInputRequestKey] = InputRequest.ForSampling(BuildSamplingRequest(promptText)),
            },
            requestState: null);
    }

    private static (RequestScopedInputOutcome Outcome, ElicitResult? Result) ClassifyElicitationResponse(
        string inputRequestKey,
        InputResponse? inputResponse,
        ILogger? logger)
    {
        ElicitResult? result;
        try
        {
            result = inputResponse?.Deserialize(InputResponse.ElicitResultJsonTypeInfo);
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
                "to an ElicitResult; classifying as malformed.", inputRequestKey);
            return (RequestScopedInputOutcome.MalformedResponse, null);
        }

        return result.IsAccepted
            ? (RequestScopedInputOutcome.Accepted, result)
            : (RequestScopedInputOutcome.DeclinedOrCancelled, result);
    }

    private static (RequestScopedInputOutcome Outcome, CreateMessageResult? Result) ClassifySamplingResponse(
        InputResponse? inputResponse,
        ILogger? logger)
    {
        CreateMessageResult? result;
        try
        {
            result = inputResponse?.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo);
        }
        catch (JsonException)
        {
            result = null;
        }

        var hasUsableText = result?.Content?
            .OfType<TextContentBlock>()
            .Any(static block => !string.IsNullOrWhiteSpace(block.Text)) == true;
        if (result is null || !hasUsableText)
        {
            logger?.LogWarning(
                "Retry request carried an input response under '{Key}' that did not deserialize " +
                "to a CreateMessageResult with non-empty text; classifying as malformed.",
                SamplingInputRequestKey);
            return (RequestScopedInputOutcome.MalformedResponse, null);
        }

        return (RequestScopedInputOutcome.Accepted, result);
    }

    private static CreateMessageRequestParams BuildSamplingRequest(string promptText)
        => new()
        {
            MaxTokens = 48,
            Temperature = 0,
            StopSequences = ["\n"],
            SystemPrompt = "Return exactly one valid C# test method identifier. No markdown, no punctuation, no explanation.",
            Messages =
            [
                new SamplingMessage
                {
                    Role = Role.User,
                    Content = [new TextContentBlock { Text = promptText }],
                },
            ],
        };

#pragma warning restore MCP9005
}
