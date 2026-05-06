using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// The single error-handling and observability boundary for every <c>tools/call</c> the
/// server dispatches. Wired in <see cref="Program"/> via
/// <c>WithRequestFilters(b =&gt; b.AddCallToolFilter(StructuredCallToolFilter.Create))</c>,
/// this filter replaces the per-tool <c>ToolErrorHandler.ExecuteAsync(...)</c> wrappers
/// that historically lived inside each handler body.
///
/// <para><b>Why a filter and not an inside-handler wrapper:</b></para>
/// <para>
/// Pre-binding failures (missing required parameter, unknown parameter name, JSON
/// deserialization of <c>arguments</c>) are thrown by the SDK's reflection-based argument
/// binder BEFORE the tool method runs. A wrapper inside the handler body therefore cannot
/// observe them; the SDK would surface a bare <c>"An error occurred invoking '&lt;tool&gt;'."</c>
/// string with no diagnostic detail. <c>AddCallToolFilter</c> wraps the entire dispatcher
/// call, so it sees binding exceptions exactly the same as handler-thrown exceptions
/// (SDK PR <c>csharp-sdk#844</c>, shipped in 0.4.0-preview.3 and carried into 1.x).
/// </para>
///
/// <para><b>Governance alignment (MCP SEP-1303):</b></para>
/// <para>
/// Both binding and handler errors are returned as <see cref="CallToolResult"/> with
/// <see cref="CallToolResult.IsError"/> set to <see langword="true"/> so the LLM sees the
/// structured envelope and can self-correct on the next turn. JSON-RPC protocol errors
/// (<c>-32602</c> and friends) are reserved for the spec-mandated cases (unknown method,
/// malformed envelope) and handled by the SDK itself, not this filter.
/// </para>
///
/// <para><b>Observability:</b></para>
/// <para>
/// Opens an <see cref="AmbientGateMetrics"/> request scope around <c>next(...)</c> so the
/// workspace execution gate can record queue/hold/stale timings, and a wall-clock
/// <see cref="Stopwatch"/> captures end-to-end elapsed time. Both the success and error
/// paths inject the snapshot as a top-level <c>_meta</c> property on the returned JSON
/// envelope via <see cref="ToolErrorHandler.InjectMetaIfPossible"/>.
/// </para>
///
/// <para><b>Elicitation fallback (MCP 2025-06-18 <c>elicitation/create</c>):</b></para>
/// <para>
/// When a tool call fails with <c>InvalidArgument: missing &lt;param&gt;</c> AND the
/// missing parameter is on the strict elicitation allowlist (currently <c>workspace_load.path</c>
/// only) AND the client declares the <c>elicitation</c> capability, the filter calls
/// <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/> to ask the
/// user for the missing value, then re-invokes the tool with the elicited value patched
/// into the arguments dictionary. Clients without elicitation capability (or users who
/// decline / cancel) fall through to the existing <c>schemaHint</c>-augmented envelope
/// (<see cref="ToolErrorHandler.ClassifyAndFormat"/>) so the existing recovery path is
/// preserved exactly. Sensitive parameters (credentials, tokens, secrets, passwords,
/// API keys, auth headers) are explicitly NOT on the allowlist — per MCP spec §
/// Elicitation security, "Servers MUST NOT request sensitive information" via
/// <c>elicitation/create</c>. See <see cref="IsSensitiveFieldName"/> and
/// <see cref="AllowedElicitationParameters"/> for the defense layers.
/// </para>
///
/// <para>
/// Reference: <c>ai_docs/references/mcp-server-best-practices.md</c>.
/// </para>
/// </summary>
internal static class StructuredCallToolFilter
{
    /// <summary>
    /// Strict allowlist of <c>(toolName, paramName)</c> pairs that may be elicited from the
    /// user via <c>elicitation/create</c>. Anything not on this list is rejected at the
    /// elicitation entry point regardless of any other heuristic — defense layer 1 (per-arg
    /// allowlist) and defense layer 2 (<see cref="IsSensitiveFieldName"/>) are both checked
    /// before any elicit request is built.
    ///
    /// <para>
    /// Adding to this list requires explicit policy review: the parameter must be
    /// non-sensitive, naturally bounded (a path, an id, a select-from-N), and the recovery
    /// shape (one-shot retry with the elicited value patched in) must be safe for the tool's
    /// idempotency semantics. As of this PR the only entry is <c>workspace_load.path</c>,
    /// per the <c>elicit-workspace-path-on-missing-required-arg</c> initiative.
    /// </para>
    /// </summary>
    private static readonly HashSet<(string Tool, string Param)> AllowedElicitationParameters =
        new()
        {
            ("workspace_load", "path"),
        };

    /// <summary>
    /// Decorator factory matching the SDK's <c>McpRequestFilter&lt;TParams, TResult&gt;</c>
    /// contract: receive <paramref name="next"/> (the dispatcher handler produced by
    /// <c>WithToolsFromAssembly</c>) and return a handler that wraps it with structured
    /// error classification and <c>_meta</c> observability.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async (context, cancellationToken) =>
        {
            var toolName = context.Params?.Name ?? "unknown";
            var logger = context.Services?
                .GetService<ILoggerFactory>()?
                .CreateLogger("RoslynMcp.StructuredCallToolFilter");

            using var metricsScope = AmbientGateMetrics.BeginRequest();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                RecordElapsed(stopwatch.ElapsedMilliseconds);
                logger?.LogInformation("Tool {ToolName} completed successfully", toolName);
                return InjectMetaIntoContent(result, toolName);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a cooperative signal, not a tool error. Let the SDK
                // translate it into the protocol-level cancellation envelope.
                logger?.LogWarning("Tool {ToolName} was cancelled", toolName);
                throw;
            }
            catch (Exception ex)
            {
                // elicit-workspace-path-on-missing-required-arg: try the elicitation
                // recovery path FIRST so a successful retry produces a normal success
                // envelope (with _meta still injected). Falls through to the existing
                // ClassifyAndFormat → schemaHint envelope when not applicable.
                var elicitResult = await TryElicitAndRetryAsync(
                    context, ex, next, logger, cancellationToken).ConfigureAwait(false);
                if (elicitResult is not null)
                {
                    stopwatch.Stop();
                    RecordElapsed(stopwatch.ElapsedMilliseconds);
                    logger?.LogInformation(
                        "Tool {ToolName} succeeded on retry after elicitation", toolName);
                    return InjectMetaIntoContent(elicitResult, toolName);
                }

                stopwatch.Stop();
                RecordElapsed(stopwatch.ElapsedMilliseconds);

                var level = IsInternalError(ex) ? LogLevel.Error : LogLevel.Warning;
                logger?.Log(level, ex, "Tool {ToolName} failed", toolName);

                return BuildErrorResult(toolName, ex);
            }
        };
    }

    /// <summary>
    /// Capability-check helper: returns <see langword="true"/> when the connected client
    /// declares the <c>elicitation</c> capability per MCP 2025-06-18 § Client Capabilities.
    /// Public so initiative #9 (<c>elicit-disambiguation-on-multi-symbol-resolve</c>) can
    /// reuse the same predicate without copy-pasting the null-coalescing dance.
    /// </summary>
    /// <param name="capabilities">
    /// The <see cref="McpServer.ClientCapabilities"/> snapshot, typically obtained as
    /// <c>context.Server.ClientCapabilities</c> inside a request filter or tool method.
    /// May be <see langword="null"/> on the server's pre-initialize path.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both <paramref name="capabilities"/> and
    /// <c>capabilities.Elicitation</c> are non-null. Zero-allocation and side-effect-free.
    /// </returns>
    public static bool HasElicitation(ClientCapabilities? capabilities) =>
        capabilities?.Elicitation is not null;

    /// <summary>
    /// Defense-in-depth predicate: returns <see langword="true"/> when the parameter name
    /// suggests credential / secret / token / password / API-key / authorization material.
    /// The primary defense is the strict <see cref="AllowedElicitationParameters"/>
    /// allowlist; this helper exists so tests can pin the policy and so any future allowlist
    /// addition is double-checked before being merged. Per MCP spec § Elicitation security,
    /// "Servers MUST NOT request sensitive information" via <c>elicitation/create</c>.
    /// </summary>
    /// <param name="paramName">Parameter name (case-insensitive comparison).</param>
    /// <returns>
    /// <see langword="true"/> when the name matches a sensitive-data pattern. Empty/null
    /// names return <see langword="false"/> — the allowlist owns the positive permission
    /// decision; this helper only owns the "do not even consider" decision.
    /// </returns>
    public static bool IsSensitiveFieldName(string? paramName)
    {
        if (string.IsNullOrEmpty(paramName)) return false;
        // Use Contains-based matching so common variants ("apiKey", "api_key", "ApiKey",
        // "authToken", "passwordHash", etc.) all classify as sensitive without
        // enumerating every casing.
        return paramName.Contains("password", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("api-key", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("auth", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("authorization", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("private_key", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || paramName.Contains("private-key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="toolName"/> + <paramref name="paramName"/>
    /// is on the strict elicitation allowlist AND the parameter name is not flagged sensitive.
    /// Both checks must pass — an entry that ends up sensitive (because someone added
    /// <c>workspace_load.token</c> to the allowlist by mistake, say) still gets refused.
    /// Public so tests can pin the allowlist policy.
    /// </summary>
    public static bool IsElicitationAllowedFor(string? toolName, string? paramName)
    {
        if (string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(paramName)) return false;
        if (IsSensitiveFieldName(paramName)) return false;
        return AllowedElicitationParameters.Contains((toolName, paramName));
    }

    /// <summary>
    /// Core of the elicitation-fallback path: when <paramref name="ex"/> is an
    /// <c>InvalidArgument</c> for a missing required parameter on a tool whose
    /// (toolName, paramName) pair is on the elicitation allowlist AND the client supports
    /// elicitation, this method asks the user for the missing value via
    /// <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/>, patches
    /// the elicited value into the request's <c>Arguments</c> dictionary, and re-invokes
    /// <paramref name="next"/>. Returns <see langword="null"/> when the recovery does not
    /// apply (any layer of the gate fails) — the caller falls through to
    /// <see cref="BuildErrorResult"/> with the existing schema-hint envelope.
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
        if (!IsElicitationAllowedFor(toolName, missingParam))
        {
            // Defense-in-depth log: explicit "refused to elicit sensitive field" branch
            // so a future audit can grep for it. Not an error — we refuse silently and
            // fall through to the normal envelope.
            if (IsSensitiveFieldName(missingParam))
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
        if (!HasElicitation(context.Server?.ClientCapabilities))
        {
            return null;
        }

        // Build the strict elicit request. Single string field ('path'), required, with a
        // descriptive prompt — the user sees a one-field form (in form mode) or navigates
        // to the URL (in url mode); the client picks based on its declared sub-capability.
        var elicitRequest = BuildPathElicitationRequest(toolName, missingParam);

        ElicitResult elicitResult;
        try
        {
            elicitResult = await context.Server!.ElicitAsync(elicitRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception elicitEx)
        {
            // ElicitAsync can throw InvalidOperationException (client doesn't support
            // elicitation despite the capability check, transport went away, etc.) or
            // McpException (client returned an error). Either way we fall through to the
            // existing envelope rather than masking the original error.
            logger?.LogWarning(elicitEx,
                "Elicitation request failed for {Tool}.{Param}; falling back to schemaHint envelope.",
                toolName, missingParam);
            return null;
        }

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
    private static ElicitRequestParams BuildPathElicitationRequest(string toolName, string paramName)
    {
        return new ElicitRequestParams
        {
            Message =
                $"The {toolName} tool was called without a '{paramName}' argument. " +
                $"Provide an absolute path to a .sln, .slnx, or .csproj file to continue.",
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    [paramName] = new ElicitRequestParams.StringSchema
                    {
                        Title = "Workspace path",
                        Description =
                            "Absolute path to a .sln, .slnx, or .csproj file on the local filesystem.",
                    },
                },
                Required = [paramName],
            },
        };
    }

    /// <summary>
    /// Produces the <see cref="CallToolResult"/> envelope the filter emits when a tool call
    /// throws. Visible to tests so the classifier→envelope→<c>_meta</c> pipeline can be
    /// asserted without standing up a real <c>RequestContext</c> / <c>McpServer</c>.
    /// </summary>
    internal static CallToolResult BuildErrorResult(string toolName, Exception ex)
    {
        var envelope = ToolErrorHandler.ClassifyAndFormat(ex, toolName);
        var envelopeWithMeta = ToolErrorHandler.InjectMetaIfPossible(envelope, toolName);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = envelopeWithMeta }],
        };
    }

    /// <summary>
    /// Injects the gate-metrics snapshot as a top-level <c>_meta</c> property on the first
    /// text content block when the tool's JSON response is object-rooted. Arrays,
    /// primitives, and non-text content are returned unchanged — preserving the
    /// historical contract that e.g. <c>source_generated_documents</c>'s bare-array
    /// response shape remains stable across the filter migration. Exposed <c>internal</c>
    /// so tests can assert meta-injection behavior directly.
    ///
    /// <para><b>tool-output-schema-infrastructure (MCP 2025-06-18 § Tools / Structured Content):</b></para>
    /// <para>
    /// When the tool has a registered <c>outputSchema</c> via
    /// <see cref="McpToolMetadataAttribute.OutputSchemaTypeRef"/>, this method ALSO populates
    /// <see cref="CallToolResult.StructuredContent"/> with the same payload (sans <c>_meta</c>) so
    /// the structured-content channel is non-empty. Per spec, when <c>structuredContent</c> is
    /// emitted the server MUST also emit a serialized JSON copy in the <c>content[].text</c>
    /// channel — both channels coexist; <c>_meta</c> lives only in the text channel so clients
    /// never see two observability blobs (defense against the dedupe risk noted in the
    /// initiative plan).
    /// </para>
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName) =>
        InjectMetaIntoContent(result, toolName, ToolOutputSchemaIndex.GetSchema);

    /// <summary>
    /// Test seam: same as <see cref="InjectMetaIntoContent(CallToolResult, string)"/> but lets
    /// the caller supply a custom schema resolver so dual-channel behavior can be exercised
    /// without needing a live <c>[McpToolMetadata(outputSchemaTypeRef:)]</c> opt-in. The
    /// production path always uses the static <see cref="ToolOutputSchemaIndex"/>.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(
        CallToolResult result, string toolName, Func<string, JsonNode?> schemaResolver)
    {
        if (result.Content is null || result.Content.Count == 0)
        {
            return result;
        }

        if (result.Content[0] is not TextContentBlock text || string.IsNullOrEmpty(text.Text))
        {
            return result;
        }

        // Parse once so both the meta-injection path and the structuredContent path can share
        // a single JsonNode tree. Non-JSON / array-rooted responses bail out early as before.
        JsonNode? parsedRoot = null;
        try
        {
            parsedRoot = JsonNode.Parse(text.Text);
        }
        catch (JsonException)
        {
            // Fall through to the original best-effort path; non-JSON responses pass through.
        }

        var schema = schemaResolver(toolName);
        // structuredContent is only emitted when (a) the tool opted in via OutputSchemaTypeRef
        // and (b) the response is an object-rooted JSON document we can mirror. Arrays, scalars,
        // and non-JSON responses leave structuredContent absent — matching the spec's "MAY"
        // semantics rather than fabricating a structured shape that doesn't match the schema.
        // CallToolResult.StructuredContent is a JsonElement? — convert from JsonNode via the
        // round-trip text. The body is small (already serialized once for the text channel)
        // so the extra parse is bounded; deep-clone to detach from the parsed tree.
        JsonElement? structuredFromBody = null;
        if (schema is not null && parsedRoot is JsonObject bodyObj)
        {
            structuredFromBody = JsonDocument.Parse(bodyObj.ToJsonString()).RootElement.Clone();
        }

        var injected = ToolErrorHandler.InjectMetaIfPossible(text.Text, toolName);
        var textChanged = !(ReferenceEquals(injected, text.Text) || injected == text.Text);

        if (!textChanged && structuredFromBody is null)
        {
            // Nothing to change — skip the allocation so array-rooted and non-JSON
            // responses pass through byte-for-byte identical.
            return result;
        }

        var newContent = new List<ContentBlock>(result.Content.Count)
        {
            new TextContentBlock { Text = textChanged ? injected : text.Text }
        };
        for (var i = 1; i < result.Content.Count; i++)
        {
            newContent.Add(result.Content[i]);
        }

        return new CallToolResult
        {
            IsError = result.IsError,
            Content = newContent,
            // Preserve any pre-existing StructuredContent (a tool may have set it directly);
            // otherwise emit the schema-mirrored body when the tool has opted in.
            StructuredContent = result.StructuredContent ?? structuredFromBody,
        };
    }

    private static void RecordElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.ElapsedMs = elapsedMs;
        }
    }

    /// <summary>
    /// Predicate for logger severity: anything that <see cref="ToolErrorHandler"/>
    /// classifies as <c>InternalError</c> becomes an Error log; known categories
    /// (InvalidArgument, NotFound, Timeout, RateLimited, InvalidOperation,
    /// WorkspaceReloadedDuringCall, ...) stay at Warning since they are caller-correctable
    /// or recoverable. We approximate the classification shallowly here — a false Warning
    /// on a rare deep-domain error is preferable to re-running the full classifier twice.
    /// </summary>
    private static bool IsInternalError(Exception ex) =>
        ex is NullReferenceException
        or InvalidCastException
        or IndexOutOfRangeException
        or StackOverflowException
        or OutOfMemoryException;
}
