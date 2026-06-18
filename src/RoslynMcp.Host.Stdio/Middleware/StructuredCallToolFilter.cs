using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

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
/// missing parameter is on the strict elicitation allowlist (currently
/// <c>workspace_load.path</c>, plus required <c>workspaceId</c> parameters on registered
/// read-only, non-destructive workspace-scoped tools) AND the client declares the
/// <c>elicitation</c> capability, the filter calls
/// <see cref="McpServer.ElicitAsync(ElicitRequestParams, CancellationToken)"/> to ask the
/// user for the missing value. For <c>workspace_load.path</c> the value is patched into
/// the original call. For missing <c>workspaceId</c>, the filter elicits a workspace path,
/// calls <c>workspace_load</c>, extracts the returned <c>workspaceId</c>, then retries the
/// original call with that id. Clients without elicitation capability (or users who
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
    private const string WorkspaceLoadToolName = "workspace_load";
    private const string WorkspaceIdParameterName = "workspaceId";
    private const string PathParameterName = "path";

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
    /// idempotency semantics. Required <c>workspaceId</c> parameters for read-only,
    /// non-destructive tools are handled by
    /// <see cref="IsWorkspaceIdRecoveryAllowedFor"/> because the concrete elicited field is
    /// <c>workspace_load.path</c>; <c>workspaceId</c> itself is a path-derived session token,
    /// not a credential, and is pinned non-sensitive by tests.
    /// </para>
    /// </summary>
    private static readonly HashSet<(string Tool, string Param)> AllowedElicitationParameters =
        new()
        {
            (WorkspaceLoadToolName, PathParameterName),
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
                // workspace-id-omitted-single-resolve: pre-dispatch workspaceId auto-resolution.
                // For read-only, non-destructive tools that declare a workspaceId parameter, a
                // call with workspaceId omitted/empty is resolved here — at the chokepoint —
                // before the SDK binder runs: exactly one workspace loaded => patch it in;
                // two-or-more => structured fast-fail listing the candidates; zero => left for
                // on-demand discovery / the binder. This is intentionally NOT gated on the
                // schema's Required flag (unlike IsWorkspaceIdRecoveryAllowedFor) so it keeps
                // working after read-only tools flip workspaceId to optional.
                var workspaceManager = context.Services?.GetService<IWorkspaceManager>();
                if (workspaceManager is not null && IsWorkspaceIdAutoResolveAllowedFor(toolName))
                {
                    if (HasNonEmptyWorkspaceId(context.Params?.Arguments))
                    {
                        // Explicit id supplied — record it and skip the loaded-workspace
                        // enumeration entirely (the common path; avoids per-call DTO projection).
                        RecordAutoResolution("explicit");
                    }
                    else
                    {
                        var loadedWorkspaces = workspaceManager.ListWorkspaces()
                            .Select(WorkspaceStatusSummaryDto.From)
                            .ToArray();
                        var resolution = ClassifyWorkspaceIdResolution(
                            context.Params?.Arguments,
                            loadedWorkspaces,
                            out var resolvedWorkspaceId,
                            out var fastFailMessage);

                        switch (resolution)
                        {
                            case WorkspaceIdAutoResolution.SingleWorkspace:
                                context.Params!.Arguments =
                                    WithWorkspaceId(context.Params.Arguments, resolvedWorkspaceId!);
                                RecordAutoResolution("single-workspace");
                                logger?.LogInformation(
                                    "Tool {ToolName} called without workspaceId; resolved to the single " +
                                    "loaded workspace {WorkspaceId}.", toolName, resolvedWorkspaceId);
                                break;

                            case WorkspaceIdAutoResolution.FastFail:
                                RecordAutoResolution("fast-fail");
                                stopwatch.Stop();
                                RecordElapsed(stopwatch.ElapsedMilliseconds);
                                logger?.LogWarning(
                                    "Tool {ToolName} called without workspaceId while {Count} workspaces " +
                                    "are loaded; returning a structured fast-fail.",
                                    toolName, loadedWorkspaces.Length);
                                return BuildErrorResult(
                                    toolName,
                                    new ArgumentException(fastFailMessage, WorkspaceIdParameterName));

                            case WorkspaceIdAutoResolution.NotApplicable:
                            {
                                // workspace-auto-load-on-demand: zero workspaces loaded — try to
                                // discover the implied solution and load it on demand before
                                // dispatch. A unique discovery patches the id and falls through to
                                // next(); an ambiguous one returns a structured fast-fail; nothing
                                // discovered falls through to the binder/elicitation path.
                                var autoLoadFastFail = await TryAutoLoadWorkspaceAsync(
                                    context, next, toolName, logger, cancellationToken).ConfigureAwait(false);
                                if (autoLoadFastFail is not null)
                                {
                                    stopwatch.Stop();
                                    RecordElapsed(stopwatch.ElapsedMilliseconds);
                                    return autoLoadFastFail;
                                }

                                break;
                            }

                            case WorkspaceIdAutoResolution.Explicit:
                                // Cannot occur here (explicit id is short-circuited before
                                // enumeration above). Defensive no-op.
                                break;
                        }
                    }
                }

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
        return AllowedElicitationParameters.Contains((toolName, paramName))
               || IsWorkspaceIdRecoveryAllowedFor(toolName, paramName);
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

        if (IsWorkspaceIdRecoveryAllowedFor(toolName, missingParam))
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

    internal static bool IsWorkspaceIdRecoveryAllowedFor(string toolName, string paramName)
    {
        if (!string.Equals(paramName, WorkspaceIdParameterName, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsSensitiveFieldName(paramName))
        {
            return false;
        }

        var schema = ToolParameterIndex.GetParameter(toolName, paramName);
        var tool = ServerSurfaceCatalog.Tools.FirstOrDefault(entry =>
            string.Equals(entry.Name, toolName, StringComparison.Ordinal));

        return schema is { Type: "string", Required: true }
               && tool is { ReadOnly: true, Destructive: false };
    }

    /// <summary>
    /// workspace-id-omitted-single-resolve: returns <see langword="true"/> when
    /// <paramref name="toolName"/> is a read-only, non-destructive tool that declares a string
    /// <c>workspaceId</c> parameter, making it eligible for pre-dispatch auto-resolution.
    /// Distinct from <see cref="IsWorkspaceIdRecoveryAllowedFor"/>: that predicate gates the
    /// exception-path elicitation recovery and requires <c>Required:true</c> (it only fires when
    /// the binder threw on a missing required arg); this one is <b>independent of the Required
    /// flag</b> so auto-resolution keeps working after a read-only tool flips <c>workspaceId</c>
    /// to optional. Public so tests can pin the policy.
    /// </summary>
    public static bool IsWorkspaceIdAutoResolveAllowedFor(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            return false;
        }

        var schema = ToolParameterIndex.GetParameter(toolName, WorkspaceIdParameterName);
        if (schema is not { Type: "string" })
        {
            return false;
        }

        var tool = ServerSurfaceCatalog.Tools.FirstOrDefault(entry =>
            string.Equals(entry.Name, toolName, StringComparison.Ordinal));
        return tool is { ReadOnly: true, Destructive: false };
    }

    /// <summary>
    /// The outcome of pre-dispatch <c>workspaceId</c> resolution for an auto-resolve-eligible
    /// read-only tool. Mirrors the <c>_meta.autoResolution</c> values minus the implicit
    /// "no resolution path" case (<see cref="NotApplicable"/>).
    /// </summary>
    internal enum WorkspaceIdAutoResolution
    {
        /// <summary>workspaceId omitted and zero workspaces loaded — leave for discovery/binder.</summary>
        NotApplicable,
        /// <summary>Caller supplied a non-empty workspaceId — left untouched.</summary>
        Explicit,
        /// <summary>workspaceId omitted and exactly one workspace loaded — patch it in.</summary>
        SingleWorkspace,
        /// <summary>workspaceId omitted and ≥2 workspaces loaded — structured fast-fail.</summary>
        FastFail,
    }

    /// <summary>
    /// workspace-id-omitted-single-resolve: classifies how an auto-resolve-eligible tool's
    /// <c>workspaceId</c> should be handled given the supplied <paramref name="arguments"/> and
    /// the currently <paramref name="loadedWorkspaces"/>. Reuses
    /// <see cref="WorkspaceTools.ResolveOptionalWorkspaceId"/> for the single-workspace decision
    /// so the chokepoint applies the exact same semantics the workspace tools do. Public so the
    /// pre-dispatch branch can be unit-tested without standing up a live MCP transport.
    /// </summary>
    /// <param name="resolvedWorkspaceId">
    /// Set to the resolved id when the result is <see cref="WorkspaceIdAutoResolution.SingleWorkspace"/>;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <param name="fastFailMessage">
    /// Set to a candidate-listing message when the result is
    /// <see cref="WorkspaceIdAutoResolution.FastFail"/>; otherwise <see langword="null"/>.
    /// </param>
    public static WorkspaceIdAutoResolution ClassifyWorkspaceIdResolution(
        IDictionary<string, JsonElement>? arguments,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        out string? resolvedWorkspaceId,
        out string? fastFailMessage)
    {
        resolvedWorkspaceId = null;
        fastFailMessage = null;

        if (HasNonEmptyWorkspaceId(arguments))
        {
            return WorkspaceIdAutoResolution.Explicit;
        }

        var resolved = WorkspaceTools.ResolveOptionalWorkspaceId(null, loadedWorkspaces);
        if (resolved is not null)
        {
            resolvedWorkspaceId = resolved;
            return WorkspaceIdAutoResolution.SingleWorkspace;
        }

        if (loadedWorkspaces.Count >= 2)
        {
            var ids = string.Join(", ", loadedWorkspaces.Select(workspace => workspace.WorkspaceId));
            fastFailMessage =
                $"workspaceId was omitted but {loadedWorkspaces.Count} workspaces are loaded ({ids}). " +
                "Pass workspaceId explicitly to choose one.";
            return WorkspaceIdAutoResolution.FastFail;
        }

        return WorkspaceIdAutoResolution.NotApplicable;
    }

    private static bool HasNonEmptyWorkspaceId(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || !arguments.TryGetValue(WorkspaceIdParameterName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static IDictionary<string, JsonElement> WithWorkspaceId(
        IDictionary<string, JsonElement>? existing, string workspaceId)
    {
        var newArgs = existing is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(existing, StringComparer.Ordinal);
        newArgs[WorkspaceIdParameterName] = JsonSerializer.SerializeToElement(workspaceId);
        return newArgs;
    }

    private static void RecordAutoResolution(string value)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.AutoResolution = value;
        }
    }

    private static void RecordAutoLoadElapsed(long elapsedMs)
    {
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.AutoLoadElapsedMs = elapsedMs;
        }
    }

    /// <summary>
    /// workspace-auto-load-on-demand: invoked from the pre-dispatch path when an auto-resolve
    /// eligible tool is called with <c>workspaceId</c> omitted and ZERO workspaces loaded.
    /// Discovers the implied solution (<see cref="SolutionDiscoveryHelper"/>):
    /// <list type="bullet">
    ///   <item><b>Unique</b> → load it via the <c>workspace_load</c> tool (reusing its dedup /
    ///   cap / eviction / progress), patch the returned id into the call, record
    ///   <c>auto-loaded</c> + <c>autoLoadElapsedMs</c>, and return <see langword="null"/> so the
    ///   caller falls through to dispatch the original tool. If the load yields no id, returns
    ///   <see langword="null"/> to fall back to the existing recovery path.</item>
    ///   <item><b>Ambiguous</b> → return a structured fast-fail listing the candidate solutions
    ///   with a ready-to-run <c>workspace_load(path=…)</c> hint (records <c>fast-fail</c>).</item>
    ///   <item><b>None</b> → return <see langword="null"/>; the binder/elicitation path handles it
    ///   (the elicitation fallback when the client supports it).</item>
    /// </list>
    /// A non-null return is a terminal fast-fail; <see langword="null"/> means "fall through to
    /// <c>next()</c>" (whether or not the arguments were patched).
    /// </summary>
    private static async Task<CallToolResult?> TryAutoLoadWorkspaceAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string toolName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var discovery = await SolutionDiscoveryHelper.TryDiscoverAsync(
            context.Params?.Arguments, context.Server, cancellationToken).ConfigureAwait(false);

        switch (discovery.Status)
        {
            case SolutionDiscoveryHelper.DiscoveryStatus.Unique:
            {
                var stopwatch = Stopwatch.StartNew();
                var loadArguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [PathParameterName] = JsonSerializer.SerializeToElement(discovery.UniquePath!),
                };
                var loadResult = await DispatchWithTemporaryArgumentsAsync(
                    context, next, WorkspaceLoadToolName, loadArguments, cancellationToken).ConfigureAwait(false);
                var workspaceId = TryExtractWorkspaceId(loadResult);
                stopwatch.Stop();

                if (string.IsNullOrWhiteSpace(workspaceId))
                {
                    logger?.LogWarning(
                        "Auto-load discovered {Path} for {Tool} but workspace_load returned no id; " +
                        "falling back to the recovery path.", discovery.UniquePath, toolName);
                    return null;
                }

                context.Params!.Arguments = WithWorkspaceId(context.Params.Arguments, workspaceId);
                RecordAutoResolution("auto-loaded");
                RecordAutoLoadElapsed(stopwatch.ElapsedMilliseconds);
                logger?.LogInformation(
                    "Tool {ToolName} called without workspaceId and none loaded; auto-loaded {Path} " +
                    "as {WorkspaceId} in {ElapsedMs}ms.",
                    toolName, discovery.UniquePath, workspaceId, stopwatch.ElapsedMilliseconds);
                return null;
            }

            case SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous:
            {
                RecordAutoResolution("fast-fail");
                var candidates = string.Join(", ", discovery.Candidates);
                logger?.LogWarning(
                    "Tool {ToolName} called without workspaceId and none loaded; {Count} candidate " +
                    "solutions discovered ({Candidates}).", toolName, discovery.Candidates.Count, candidates);
                return BuildErrorResult(toolName, new ArgumentException(
                    $"workspaceId was omitted and no workspace is loaded. {discovery.Candidates.Count} " +
                    $"candidate solutions were discovered ({candidates}). Call workspace_load(path=…) with " +
                    "one of them, then retry — or pass workspaceId explicitly.",
                    WorkspaceIdParameterName));
            }

            case SolutionDiscoveryHelper.DiscoveryStatus.None:
            default:
                return null;
        }
    }

    internal static async Task<CallToolResult?> TryRecoverMissingWorkspaceIdAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? originalArguments,
        Func<ElicitRequestParams, ValueTask<ElicitResult>> elicitAsync,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> dispatchAsync,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var elicitRequest = BuildWorkspacePathElicitationRequest(toolName, WorkspaceIdParameterName);
        ElicitResult elicitResult;
        try
        {
            elicitResult = await elicitAsync(elicitRequest).ConfigureAwait(false);
        }
        catch (Exception elicitEx)
        {
            logger?.LogWarning(elicitEx,
                "WorkspaceId recovery elicitation failed for {Tool}; falling back to schemaHint envelope.",
                toolName);
            return null;
        }

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
        CallToolResult loadResult;
        try
        {
            loadResult = await dispatchAsync(WorkspaceLoadToolName, loadArgs).ConfigureAwait(false);
        }
        catch (Exception loadEx)
        {
            logger?.LogWarning(loadEx,
                "workspace_load dispatch threw during workspaceId recovery for {Tool}; falling back to schemaHint envelope.",
                toolName);
            return null;
        }

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
        try
        {
            return await dispatchAsync(toolName, retryArgs).ConfigureAwait(false);
        }
        catch (Exception retryEx)
        {
            logger?.LogWarning(retryEx,
                "Retried tool dispatch threw during workspaceId recovery for {Tool}; falling back to schemaHint envelope.",
                toolName);
            return null;
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

    private static async Task<CallToolResult> DispatchWithTemporaryArgumentsAsync(
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

    private static string? TryExtractWorkspaceId(CallToolResult result)
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

    /// <summary>
    /// elicit-disambiguation-on-multi-symbol-resolve: shared select-from-N elicitation
    /// helper. Builds an enum-shaped <c>elicitation/create</c> request whose options carry
    /// short candidate labels and stable string keys, calls the SDK, and returns the chosen
    /// key (or <see langword="null"/> when the user declined / the client lacks elicitation /
    /// the request itself failed). The caller uses the returned key to map back to the
    /// original candidate (e.g. an <c>ISymbol</c>) and re-runs the tool call against that
    /// chosen candidate.
    /// </summary>
    /// <param name="server">The connected <see cref="McpServer"/>; <see langword="null"/> short-circuits to null.</param>
    /// <param name="paramName">
    /// Name of the schema field carrying the picked option — also the dictionary key the
    /// SDK populates in <see cref="ElicitResult.Content"/>. Conventionally <c>"choice"</c>.
    /// </param>
    /// <param name="title">Short title shown above the option list.</param>
    /// <param name="description">Longer description (one or two sentences) explaining why the user is being asked.</param>
    /// <param name="options">
    /// The select-from-N options. <c>Key</c> is the stable identifier returned to the caller,
    /// <c>Label</c> is the human-readable text shown in the picker.
    /// </param>
    /// <param name="cancellationToken">Cancellation token (request-scoped).</param>
    /// <returns>The chosen <c>Key</c> on accept; <see langword="null"/> on decline / cancel / unsupported / error.</returns>
    public static async Task<string?> TryElicitChoiceAsync(
        McpServer? server,
        string paramName,
        string title,
        string description,
        IReadOnlyList<(string Key, string Label)> options,
        CancellationToken cancellationToken)
    {
        if (server is null) return null;
        if (!HasElicitation(server.ClientCapabilities)) return null;
        if (string.IsNullOrEmpty(paramName) || options is null || options.Count == 0) return null;

        var oneOf = new List<ElicitRequestParams.EnumSchemaOption>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            var (key, label) = options[i];
            if (string.IsNullOrEmpty(key)) continue;
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

        ElicitResult result;
        try
        {
            result = await server.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ElicitAsync can throw InvalidOperationException (transport gone, capability
            // mismatch) or McpException (client-side error). Either way, the disambiguation
            // path falls through to the additive list response — never masks a real failure.
            return null;
        }

        if (!result.IsAccepted || result.Content is null) return null;
        if (!result.Content.TryGetValue(paramName, out var chosen)) return null;
        var chosenKey = chosen.ValueKind == JsonValueKind.String ? chosen.GetString() : null;
        return string.IsNullOrEmpty(chosenKey) ? null : chosenKey;
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
