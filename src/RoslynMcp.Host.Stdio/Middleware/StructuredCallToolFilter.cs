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
/// <c>elicitation/create</c>. See <see cref="ElicitationAllowlistPolicy.IsSensitiveFieldName"/>
/// and <see cref="ElicitationAllowlistPolicy"/> for the defense layers.
/// </para>
///
/// <para>
/// Reference: <c>ai_docs/references/mcp-server-best-practices.md</c>.
/// </para>
/// </summary>
internal static class StructuredCallToolFilter
{
    // Shared with ElicitationAllowlistPolicy (which duplicates these consts — a private const
    // does not cross the class boundary). Retained here because the filter's dispatch/recovery
    // body still references all three (workspace_load dispatch, workspaceId patching, path elicit).
    private const string WorkspaceLoadToolName = "workspace_load";
    private const string WorkspaceIdParameterName = "workspaceId";
    private const string PathParameterName = "path";

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
                // schema's Required flag (like IsWorkspaceIdRecoveryAllowedFor, which is also
                // Required-independent) so it keeps working after read-only tools flip
                // workspaceId to optional.
                var workspaceManager = context.Services?.GetService<IWorkspaceManager>();
                if (workspaceManager is not null && IsWorkspaceIdAutoResolveAllowedFor(toolName))
                {
                    if (HasNonEmptyWorkspaceId(context.Params?.Arguments))
                    {
                        // Explicit id supplied — record it and skip the loaded-workspace
                        // enumeration entirely (the common path; avoids per-call DTO projection).
                        CallMetricsRecorder.RecordAutoResolution("explicit");
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
                                CallMetricsRecorder.RecordAutoResolution("single-workspace");
                                logger?.LogInformation(
                                    "Tool {ToolName} called without workspaceId; resolved to the single " +
                                    "loaded workspace {WorkspaceId}.", toolName, resolvedWorkspaceId);
                                break;

                            case WorkspaceIdAutoResolution.FastFail:
                                CallMetricsRecorder.RecordAutoResolution("fast-fail");
                                stopwatch.Stop();
                                CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
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
                                        CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
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
                CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
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
                    CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                    logger?.LogInformation(
                        "Tool {ToolName} succeeded on retry after elicitation", toolName);
                    return InjectMetaIntoContent(elicitResult, toolName);
                }

                stopwatch.Stop();
                CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);

                var level = IsInternalError(ex) ? LogLevel.Error : LogLevel.Warning;
                logger?.Log(level, ex, "Tool {ToolName} failed", toolName);

                return BuildErrorResult(toolName, ex);
            }
        };
    }

    /// <summary>
    /// Thin delegate preserving the historical static call surface. The policy lives in
    /// <see cref="ElicitationAllowlistPolicy.HasElicitation"/>; kept here so existing callers
    /// (<c>SymbolTools</c>, the filter test suites) compile unchanged.
    /// </summary>
    public static bool HasElicitation(ClientCapabilities? capabilities) =>
        ElicitationAllowlistPolicy.HasElicitation(capabilities);

    /// <summary>
    /// Thin delegate preserving the historical static call surface. See
    /// <see cref="ElicitationAllowlistPolicy.IsSensitiveFieldName"/>.
    /// </summary>
    public static bool IsSensitiveFieldName(string? paramName) =>
        ElicitationAllowlistPolicy.IsSensitiveFieldName(paramName);

    /// <summary>
    /// Thin delegate preserving the historical static call surface. See
    /// <see cref="ElicitationAllowlistPolicy.IsElicitationAllowedFor"/>.
    /// </summary>
    public static bool IsElicitationAllowedFor(string? toolName, string? paramName) =>
        ElicitationAllowlistPolicy.IsElicitationAllowedFor(toolName, paramName);

    /// <summary>
    /// Thin delegate preserving the historical static call surface. The elicitation/retry
    /// orchestration lives in <see cref="StructuredCallElicitationCoordinator.TryElicitAndRetryAsync"/>;
    /// kept here so <see cref="Create"/> and the existing filter test suites compile unchanged.
    /// </summary>
    internal static Task<CallToolResult?> TryElicitAndRetryAsync(
        RequestContext<CallToolRequestParams> context,
        Exception ex,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        ILogger? logger,
        CancellationToken cancellationToken) =>
        StructuredCallElicitationCoordinator.TryElicitAndRetryAsync(context, ex, next, logger, cancellationToken);

    /// <summary>
    /// Thin delegate preserving the historical static call surface. See
    /// <see cref="ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor"/>.
    /// </summary>
    internal static bool IsWorkspaceIdRecoveryAllowedFor(string toolName, string paramName) =>
        ElicitationAllowlistPolicy.IsWorkspaceIdRecoveryAllowedFor(toolName, paramName);

    /// <summary>
    /// Thin delegate preserving the historical static call surface. See
    /// <see cref="ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor"/>.
    /// </summary>
    public static bool IsWorkspaceIdAutoResolveAllowedFor(string? toolName) =>
        ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(toolName);

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
    ///   <item><b>None</b> → return <see langword="null"/> to fall through to <c>next()</c>; the
    ///   downstream tool then either binds the supplied <c>workspaceId</c> or, when it is omitted on
    ///   an auto-resolve-eligible read-only tool, throws and triggers the exception-path elicitation
    ///   recovery gated by <see cref="IsWorkspaceIdRecoveryAllowedFor"/> (which is Required-independent,
    ///   so it stays live for tools that flipped <c>workspaceId</c> to optional).</item>
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
                    var loadResult = await StructuredCallElicitationCoordinator.DispatchWithTemporaryArgumentsAsync(
                        context, next, WorkspaceLoadToolName, loadArguments, cancellationToken).ConfigureAwait(false);
                    var workspaceId = StructuredCallElicitationCoordinator.TryExtractWorkspaceId(loadResult);
                    stopwatch.Stop();

                    if (string.IsNullOrWhiteSpace(workspaceId))
                    {
                        logger?.LogWarning(
                            "Auto-load discovered {Path} for {Tool} but workspace_load returned no id; " +
                            "falling back to the recovery path.", discovery.UniquePath, toolName);
                        return null;
                    }

                    context.Params!.Arguments = WithWorkspaceId(context.Params.Arguments, workspaceId);
                    CallMetricsRecorder.RecordAutoResolution("auto-loaded");
                    CallMetricsRecorder.RecordAutoLoadElapsed(stopwatch.ElapsedMilliseconds);
                    logger?.LogInformation(
                        "Tool {ToolName} called without workspaceId and none loaded; auto-loaded {Path} " +
                        "as {WorkspaceId} in {ElapsedMs}ms.",
                        toolName, discovery.UniquePath, workspaceId, stopwatch.ElapsedMilliseconds);
                    return null;
                }

            case SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous:
                {
                    CallMetricsRecorder.RecordAutoResolution("fast-fail");
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

    /// <summary>
    /// Thin delegate preserving the historical static call surface. The recover-load-retry
    /// loop lives in
    /// <see cref="StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync"/>;
    /// kept here so the existing filter test suites compile unchanged.
    /// </summary>
    internal static Task<CallToolResult?> TryRecoverMissingWorkspaceIdAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement>? originalArguments,
        Func<ElicitRequestParams, ValueTask<ElicitResult>> elicitAsync,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> dispatchAsync,
        ILogger? logger,
        CancellationToken cancellationToken) =>
        StructuredCallElicitationCoordinator.TryRecoverMissingWorkspaceIdAsync(
            toolName, originalArguments, elicitAsync, dispatchAsync, logger, cancellationToken);

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
    public static Task<string?> TryElicitChoiceAsync(
        McpServer? server,
        string paramName,
        string title,
        string description,
        IReadOnlyList<(string Key, string Label)> options,
        CancellationToken cancellationToken) =>
        StructuredCallElicitationCoordinator.TryElicitChoiceAsync(
            server, paramName, title, description, options, cancellationToken);

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
    /// Thin delegate preserving the historical static call surface. The structured-content /
    /// <c>_meta</c> projection lives in
    /// <see cref="StructuredCallContentProjector.InjectMetaIntoContent(CallToolResult, string)"/>;
    /// kept here so <see cref="Create"/> and the existing filter/content test suites compile unchanged.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName) =>
        StructuredCallContentProjector.InjectMetaIntoContent(result, toolName);

    /// <summary>
    /// Thin delegate preserving the historical static call surface (test seam with a custom
    /// schema resolver). See
    /// <see cref="StructuredCallContentProjector.InjectMetaIntoContent(CallToolResult, string, Func{string, JsonNode})"/>.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(
        CallToolResult result, string toolName, Func<string, JsonNode?> schemaResolver) =>
        StructuredCallContentProjector.InjectMetaIntoContent(result, toolName, schemaResolver);

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
