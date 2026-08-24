using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
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
/// <para><b>Request-scoped input recovery:</b></para>
/// <para>
/// Before binding, the filter detects an omitted allowlisted field (currently
/// <c>workspace_load.path</c>, plus <c>workspaceId</c> on registered read-only,
/// non-destructive tools). <see cref="RequestScopedInputAdapter"/> then emits an MRTR
/// <see cref="InputRequiredResult"/> on 2026-07-28 sessions or uses the legacy nested
/// <c>elicitation/create</c> continuation on older stateful sessions. Accepted path input is
/// patched into <c>workspace_load</c>; workspaceId recovery loads the path and retries with the
/// returned id. Declined, malformed, or unsupported input falls through to the existing
/// schema-hint error envelope. Sensitive field names remain fail-closed in
/// <see cref="ElicitationAllowlistPolicy"/>.
/// </para>
///
/// <para><b>MRTR passthrough (SEP-2322, protocol 2026-07-28):</b></para>
/// <para>
/// <see cref="ModelContextProtocol.Protocol.InputRequiredException"/> is a protocol signal —
/// "this call needs client input before it can complete" — not a tool failure. The filter
/// rethrows it (like cancellation) so the SDK emits an
/// <see cref="ModelContextProtocol.Protocol.InputRequiredResult"/>; on MRTR-capable sessions
/// the client resolves the embedded input requests and retries the call carrying
/// <c>params.inputResponses</c>, which
/// <see cref="Elicitation.RequestScopedInputAdapter"/> consumes request-scoped. Sessions that
/// negotiated 2025-11-25 or earlier keep the direct <c>elicitation/create</c> path above.
/// </para>
///
/// <para>
/// Reference: <c>ai_docs/references/mcp-server-best-practices.md</c>.
/// </para>
/// </summary>
internal static class StructuredCallToolFilter
{
    /// <summary>
    /// Decorator factory matching the SDK's <c>McpRequestFilter&lt;TParams, TResult&gt;</c>
    /// contract: receive <paramref name="next"/> (the handler already bound to the selected
    /// tool) and return a handler that wraps it with structured error classification and
    /// <c>_meta</c> observability. Cross-tool recovery resolves a separate registered
    /// <see cref="McpServerTool"/> explicitly; changing <c>context.Params.Name</c> does not reroute
    /// this bound delegate.
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
            using var serverScope = RequestMcpServerContext.Begin(context.Server);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (context.Params is not null)
                {
                    context.Params.Arguments = LspSourceLocationArgumentNormalizer.Normalize(
                        toolName,
                        context.Params.Arguments);
                }

                // workspace-path-mrtr-adoption: inspect the raw arguments before the SDK binder.
                // A missing required parameter is otherwise surfaced as ParamName="arguments",
                // which cannot be safely mapped back to the allowlisted path field.
                var workspacePathRecovery = await StructuredCallElicitationCoordinator
                    .TryRecoverMissingWorkspacePathAsync(
                        context,
                        next,
                        logger,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (workspacePathRecovery is not null)
                {
                    stopwatch.Stop();
                    CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                    return InjectMetaIntoContent(
                        ApplyProtocolResultShape(context, workspacePathRecovery),
                        toolName);
                }

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
                if (workspaceManager is not null &&
                    ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(toolName))
                {
                    var workspaceIdMissingOrBlank =
                        IsWorkspaceIdMissingOrBlank(context.Params?.Arguments);
                    var hasAuthoritativeWorkspacePathResponse =
                        workspaceIdMissingOrBlank && HasAuthoritativeWorkspacePathResponse(context);
                    var restoredWorkspaceFromRequestState = false;
                    if (!hasAuthoritativeWorkspacePathResponse &&
                        workspaceIdMissingOrBlank &&
                        RequestProtocolFeatureGate.SupportsJuly2026Features(context) &&
                        RequestStateCodec.TryRestoreWorkspaceId(
                            context.Params?.RequestState,
                            out var requestStateWorkspaceId))
                    {
                        context.Params!.Arguments = WithWorkspaceId(
                            context.Params.Arguments,
                            requestStateWorkspaceId);
                        CallMetricsRecorder.RecordAutoResolution("request-state");
                        restoredWorkspaceFromRequestState = true;
                    }

                    if (hasAuthoritativeWorkspacePathResponse)
                    {
                        // A modern MRTR path response belongs to this logical request and wins
                        // over both echoed state and ambient workspace state. Genuine path
                        // recovery emits no state, but fail closed if a client combines them
                        // rather than silently discarding the operator's accepted path.
                        var recovered = await TryRecoverMissingWorkspaceIdFromPathAsync(
                            context,
                            next,
                            toolName,
                            logger,
                            cancellationToken).ConfigureAwait(false);
                        if (recovered is not null)
                        {
                            stopwatch.Stop();
                            CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                            return InjectMetaIntoContent(
                                ApplyProtocolResultShape(context, recovered),
                                toolName);
                        }

                        // Declined or malformed request-scoped input is authoritative too: do
                        // not silently replace it with a concurrently loaded workspace. Leave
                        // workspaceId absent so the normal binder emits InvalidArgument.
                    }
                    else if (!IsWorkspaceIdMissingOrBlank(context.Params?.Arguments))
                    {
                        // Explicit id supplied — record it and skip the loaded-workspace
                        // enumeration entirely (the common path; avoids per-call DTO projection).
                        if (!restoredWorkspaceFromRequestState)
                        {
                            CallMetricsRecorder.RecordAutoResolution("explicit");
                        }
                    }
                    else
                    {
                        var loadedWorkspaces = workspaceManager.ListWorkspaces()
                            .Select(WorkspaceStatusSummaryDto.From)
                            .ToArray();
                        var filePathOwnerIds = TryGetStringArgument(
                            context.Params?.Arguments,
                            "filePath") is { } filePath
                            ? workspaceManager.FindWorkspaceIdsContainingFile(filePath)
                            : [];
                        var resolution = ClassifyWorkspaceIdResolution(
                            context.Params?.Arguments,
                            loadedWorkspaces,
                            filePathOwnerIds,
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

                            case WorkspaceIdAutoResolution.FilePathWorkspace:
                                context.Params!.Arguments =
                                    WithWorkspaceId(context.Params.Arguments, resolvedWorkspaceId!);
                                CallMetricsRecorder.RecordAutoResolution("file-path");
                                logger?.LogInformation(
                                    "Tool {ToolName} called without workspaceId; resolved filePath to " +
                                    "workspace {WorkspaceId}.", toolName, resolvedWorkspaceId);
                                break;

                            case WorkspaceIdAutoResolution.FastFail:
                                CallMetricsRecorder.RecordAutoResolution("fast-fail");
                                stopwatch.Stop();
                                CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                                logger?.LogWarning(
                                    "Tool {ToolName} called without workspaceId while {Count} workspaces " +
                                    "are loaded; returning a structured fast-fail.",
                                    toolName, loadedWorkspaces.Length);
                                return ApplyProtocolResultShape(
                                    context,
                                    BuildErrorResult(
                                        toolName,
                                        new PublicArgumentException(
                                            fastFailMessage!,
                                            ElicitationAllowlistPolicy.WorkspaceIdParameterName)));

                            case WorkspaceIdAutoResolution.NotApplicable:
                                {
                                    // workspace-auto-load-on-demand: zero workspaces loaded — try to
                                    // discover the implied solution and load it on demand before
                                    // dispatch. A unique discovery patches the id and falls through to
                                    // next(); an ambiguous one returns a structured fast-fail; nothing
                                    // discovered falls through to request-scoped path recovery.
                                    var autoLoadFastFail = await TryAutoLoadWorkspaceAsync(
                                        context, toolName, logger, cancellationToken).ConfigureAwait(false);
                                    if (autoLoadFastFail is not null)
                                    {
                                        stopwatch.Stop();
                                        CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                                        return ApplyProtocolResultShape(context, autoLoadFastFail);
                                    }

                                    if (IsWorkspaceIdMissingOrBlank(context.Params?.Arguments) &&
                                        ElicitationChoicePrompt.SupportsElicitation(context))
                                    {
                                        var recovered = await TryRecoverMissingWorkspaceIdFromPathAsync(
                                            context,
                                            next,
                                            toolName,
                                            logger,
                                            cancellationToken).ConfigureAwait(false);
                                        if (recovered is not null)
                                        {
                                            stopwatch.Stop();
                                            CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);
                                            return InjectMetaIntoContent(
                                                ApplyProtocolResultShape(context, recovered),
                                                toolName);
                                        }
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
                return InjectMetaIntoContent(
                    ApplyProtocolResultShape(context, result),
                    toolName);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is a cooperative signal, not a tool error. Let the SDK
                // translate it into the protocol-level cancellation envelope.
                logger?.LogWarning("Tool {ToolName} was cancelled", toolName);
                throw;
            }
            catch (InputRequiredException inputRequired)
            {
                // MRTR (SEP-2322): an input-required signal is a protocol result, not a tool
                // error. Rethrow so the SDK converts it into an InputRequiredResult; converting
                // it into an isError CallToolResult here would make server-driven input
                // structurally impossible on MRTR sessions. MUST stay above the general
                // catch (Exception) below — C# picks the first matching clause.
                // Preserve a workspace identity that this filter resolved before the tool asked
                // for another input. The client echoes this non-secret, client-visible state on
                // retry so ambient workspace changes cannot rebind the logical call.
                RequestStateCodec.PreserveWorkspaceId(
                    inputRequired,
                    context.Params?.Arguments,
                    ElicitationAllowlistPolicy.WorkspaceIdParameterName);
                logger?.LogInformation("Tool {ToolName} returned an input-required signal", toolName);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                CallMetricsRecorder.RecordElapsed(stopwatch.ElapsedMilliseconds);

                var isInternalError = IsInternalError(ex);
                var level = isInternalError ? LogLevel.Error : LogLevel.Warning;
                if (isInternalError &&
                    context.Services?.GetService<IUnexpectedExceptionReporter>() is { } reporter)
                {
                    reporter.ReportUnexpected(
                        ex,
                        UnexpectedExceptionCategory.ToolCall);
                }

                logger?.Log(
                    level,
                    "Tool {ToolName} failed with {FailureKind}; correlationId={CorrelationId}",
                    toolName,
                    isInternalError ? "unexpected-error" : "expected-error",
                    RequestCorrelationContext.Current ?? "unavailable");

                // tool-call-error-envelope-wire-contract: failure envelopes are era-shaped
                // exactly like success envelopes. Without this, a legacy (2025-11-25) session
                // received `resultType: "complete"` on every error frame — a field that
                // protocol era does not define — because BuildErrorResult constructs a
                // CallToolResult directly and the SDK stamps the discriminator by default.
                return ApplyProtocolResultShape(context, BuildErrorResult(toolName, ex));
            }
        };
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
        /// <summary>workspaceId omitted and filePath belongs to exactly one loaded workspace.</summary>
        FilePathWorkspace,
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
        out string? fastFailMessage) =>
        ClassifyWorkspaceIdResolution(
            arguments,
            loadedWorkspaces,
            filePathOwnerIds: [],
            out resolvedWorkspaceId,
            out fastFailMessage);

    public static WorkspaceIdAutoResolution ClassifyWorkspaceIdResolution(
        IDictionary<string, JsonElement>? arguments,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        IReadOnlyList<string> filePathOwnerIds,
        out string? resolvedWorkspaceId,
        out string? fastFailMessage)
    {
        resolvedWorkspaceId = null;
        fastFailMessage = null;

        if (!IsWorkspaceIdMissingOrBlank(arguments))
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
            var ownerIds = filePathOwnerIds.ToHashSet(StringComparer.Ordinal);
            if (ownerIds.Count == 1)
            {
                resolvedWorkspaceId = ownerIds.Single();
                return WorkspaceIdAutoResolution.FilePathWorkspace;
            }

            var candidates = ownerIds.Count > 1
                ? loadedWorkspaces.Where(workspace => ownerIds.Contains(workspace.WorkspaceId)).ToArray()
                : loadedWorkspaces;
            var choices = FormatWorkspaceChoices(candidates);
            fastFailMessage =
                $"workspaceId was omitted and filePath did not identify one loaded workspace. " +
                $"Candidates: {choices}. Pass workspaceId explicitly; call workspace_list to refresh choices.";
            return WorkspaceIdAutoResolution.FastFail;
        }

        return WorkspaceIdAutoResolution.NotApplicable;
    }

    private static string FormatWorkspaceChoices(
        IEnumerable<WorkspaceStatusSummaryDto> workspaces)
    {
        const int maxChoices = 8;
        const int maxPathLength = 512;
        var ordered = workspaces
            .OrderBy(workspace => workspace.LoadedPath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(workspace => workspace.WorkspaceId, StringComparer.Ordinal)
            .ToArray();
        var formatted = ordered.Take(maxChoices).Select(workspace => JsonSerializer.Serialize(new
        {
            workspaceId = workspace.WorkspaceId,
            loadedPath = workspace.LoadedPath is { Length: > maxPathLength } path
                ? path[..maxPathLength] + "…"
                : workspace.LoadedPath,
        }));
        var suffix = ordered.Length > maxChoices
            ? $", {ordered.Length - maxChoices} more omitted"
            : string.Empty;
        return string.Join(", ", formatted) + suffix;
    }

    private static string? TryGetStringArgument(
        IDictionary<string, JsonElement>? arguments,
        string name) =>
        arguments is not null &&
        arguments.TryGetValue(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// Returns whether <c>workspaceId</c> is genuinely absent or blank and therefore eligible
    /// for resolution/recovery. A present value of any other JSON kind is caller input, even
    /// though invalid for the string parameter; preserving it lets the binder produce the
    /// canonical <c>InvalidArgument</c> envelope instead of silently overwriting it.
    /// </summary>
    private static bool IsWorkspaceIdMissingOrBlank(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null ||
            !arguments.TryGetValue(ElicitationAllowlistPolicy.WorkspaceIdParameterName, out var value))
        {
            return true;
        }

        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
               (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));
    }

    private static bool HasAuthoritativeWorkspacePathResponse(
        RequestContext<CallToolRequestParams> context) =>
        RequestProtocolFeatureGate.SupportsJuly2026Features(context) &&
        ElicitationChoicePrompt.SupportsElicitation(context) &&
        context.Params?.InputResponses?.ContainsKey(
            RequestScopedInputAdapter.WorkspacePathInputRequestKey) is true;

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

    private static IDictionary<string, JsonElement> WithWorkspaceId(
        IDictionary<string, JsonElement>? existing, string workspaceId)
    {
        var newArgs = existing is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(existing, StringComparer.Ordinal);
        newArgs[ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
            JsonSerializer.SerializeToElement(workspaceId);
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
    ///   <item><b>None</b> → return <see langword="null"/> so the caller can attempt request-scoped
    ///   path recovery before the original bound handler receives the still-missing id.</item>
    /// </list>
    /// A non-null return is a terminal fast-fail; <see langword="null"/> means the caller
    /// continues its recovery/dispatch pipeline (whether or not the arguments were patched).
    /// </summary>
    private static async Task<CallToolResult?> TryAutoLoadWorkspaceAsync(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var discovery = await AwaitRecoveryStageAsync(
            SolutionDiscoveryHelper.TryDiscoverAsync(
                context.Params?.Arguments,
                context.Server,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        switch (discovery.Status)
        {
            case SolutionDiscoveryHelper.DiscoveryStatus.Unique:
                {
                    var stopwatch = Stopwatch.StartNew();
                    var loadArguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        [ElicitationAllowlistPolicy.PathParameterName] =
                            JsonSerializer.SerializeToElement(discovery.UniquePath!),
                    };
                    var loadResult = await AwaitRecoveryStageAsync(
                        InvokeRegisteredToolWithTemporaryArgumentsAsync(
                            context,
                            ElicitationAllowlistPolicy.WorkspaceLoadToolName,
                            loadArguments,
                            cancellationToken),
                        cancellationToken).ConfigureAwait(false);
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
                    return BuildErrorResult(toolName, new PublicArgumentException(
                        $"workspaceId was omitted and no workspace is loaded. {discovery.Candidates.Count} " +
                        $"candidate solutions were discovered ({candidates}). Call workspace_load(path=…) with " +
                        "one of them, then retry — or pass workspaceId explicitly.",
                        ElicitationAllowlistPolicy.WorkspaceIdParameterName));
                }

            case SolutionDiscoveryHelper.DiscoveryStatus.None:
            default:
                return null;
        }
    }

    /// <summary>
    /// Awaits one multi-stage recovery operation and rechecks cancellation before its nominal
    /// result can drive the next stage or mutate request arguments. This closes the race where a
    /// collaborator cancels while returning a value rather than throwing.
    /// </summary>
    internal static async Task<T> AwaitRecoveryStageAsync<T>(
        Task<T> stage,
        CancellationToken cancellationToken)
    {
        var result = await stage.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>
    /// Invokes a named SDK tool primitive from the registered collection while preserving the
    /// current request context. Request-filter <c>next</c> delegates are already bound to their
    /// original tool, so mutating <c>Params.Name</c> and calling <c>next</c> cannot perform a
    /// cross-tool dispatch. The primitive is invoked directly to avoid recursively applying the
    /// outer filter; its exception remains owned by the recovery caller.
    /// </summary>
    internal static async Task<CallToolResult> InvokeRegisteredToolWithTemporaryArgumentsAsync(
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
        var originalArgs = context.Params.Arguments;
        try
        {
            context.Params.Name = toolName;
            context.Params.Arguments = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
            return await tool.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
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
    /// kept here to preserve the historical internal call surface used by <see cref="Create"/>.
    /// </summary>
    internal static CallToolResult InjectMetaIntoContent(CallToolResult result, string toolName) =>
        StructuredCallContentProjector.InjectMetaIntoContent(result, toolName);

    /// <summary>
    /// Removes the July 2026 result discriminator for legacy sessions. Explicit
    /// <see cref="CallToolResult"/> producers bypass the SDK's normal result construction, so
    /// leaving their discriminator intact would leak a field the negotiated protocol does not
    /// define. Legacy sessions also require non-object structured content to be carried under the
    /// advertised <c>{ "result": ... }</c> object envelope; modern sessions keep the natural root.
    /// </summary>
    private static CallToolResult ApplyProtocolResultShape(
        RequestContext<CallToolRequestParams> context,
        CallToolResult result)
    {
        if (!RequestProtocolFeatureGate.SupportsJuly2026Features(context))
        {
            result.ResultType = null;
            if (result.StructuredContent is { ValueKind: not JsonValueKind.Object } structuredContent)
            {
                result.StructuredContent = JsonSerializer.SerializeToElement(
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["result"] = structuredContent,
                    });
            }
        }

        return result;
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
