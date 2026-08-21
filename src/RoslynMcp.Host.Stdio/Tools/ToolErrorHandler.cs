using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Host.Stdio.Tools;

internal static class MetaSerializer
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}

internal static class ToolErrorHandler
{
    /// <summary>
    /// The canonical set of error-category strings this classifier emits. Every category the
    /// classification tables (and <see cref="ClassifyError"/>'s post-classification hooks)
    /// produce is declared here, and <c>ResourceReadResultFilter.MapErrorCode</c> gives each
    /// current category an explicit switch arm. Keep both surfaces synchronized when changing
    /// this set; the resource mapper retains an <c>InternalError</c> fallback for defensive
    /// handling of an unknown runtime value.
    /// <list type="bullet">
    ///   <item><description>Call sites reuse these constants instead of duplicating wire-visible
    ///         string literals.</description></item>
    ///   <item><description>A newly added category needs an explicit mapper decision: caller
    ///         fault (-32602) or server fault (-32603).</description></item>
    /// </list>
    /// </summary>
    internal static class ErrorCategories
    {
        public const string NotFound = "NotFound";
        public const string FileNotFound = "FileNotFound";
        public const string DirectoryNotFound = "DirectoryNotFound";
        public const string PermissionDenied = "PermissionDenied";
        public const string WorkspaceEvicted = "WorkspaceEvicted";
        public const string InvalidArgument = "InvalidArgument";
        public const string InternalError = "InternalError";
        public const string StaleWorkspaceTransition = "StaleWorkspaceTransition";
        public const string Timeout = "Timeout";
        public const string PreviewTokenStale = "PreviewTokenStale";
        public const string Disconnected = "Disconnected";
        public const string RateLimited = "RateLimited";
        public const string InvalidOperation = "InvalidOperation";
        public const string WorkspaceReloadedDuringCall = "WorkspaceReloadedDuringCall";
    }

    private static readonly Dictionary<Type, Func<Exception, string, ErrorInfo>> _errorHandlers = new()
    {
        // autoreload-cascade-stdio-host-crash: wraps a state-read that raced with (or followed)
        // an auto-reload transition. Registered BEFORE the generic Exception handlers so the
        // structured category wins over the dictionary walk's InvalidOperation fallback.
        [typeof(StaleWorkspaceTransitionException)] = (_, _) => new(ErrorCategories.StaleWorkspaceTransition,
            "Workspace is transitioning between snapshots. Retry the request against the refreshed workspace."),
        // mcp-error-category-workspace-evicted-on-host-recycle: a workspace lookup miss that
        // originated from a host recycle (graceful prior-process exit) or an explicit Close.
        // The public envelope intentionally omits timestamps and the prior path; clients retain
        // their own load input and use the stable recovery action. WorkspaceEvictedException
        // derives from KeyNotFoundException so existing catch-sites still observe it as a lookup
        // miss; register it BEFORE the base entry so the more-specific category wins.
        [typeof(WorkspaceEvictedException)] = (ex, _) =>
        {
            return new(ErrorCategories.WorkspaceEvicted,
                "The workspace session is no longer available because it was closed, evicted, or owned by a prior host process. " +
                "Call workspace_load with the original solution or project path, then retry with the new workspaceId.");
        },
        [typeof(FileNotFoundException)] = (_, _) => new(ErrorCategories.FileNotFound,
            "The requested file was not found. Verify the file path is absolute and the file exists on disk. " +
            "If the workspace was recently reloaded, the file may have been removed."),
        [typeof(DirectoryNotFoundException)] = (_, _) => new(ErrorCategories.DirectoryNotFound,
            "The requested directory was not found. Verify the directory path is absolute and exists on disk."),
        [typeof(UnauthorizedAccessException)] = (_, _) => new(ErrorCategories.PermissionDenied,
            "The operation was denied by the configured access policy or operating-system permissions. " +
            "Verify the requested path is within an allowed root and that the server account has access."),
        [typeof(KeyNotFoundException)] = (ex, _) => new(ErrorCategories.NotFound, BuildSafeNotFoundMessage(ex.Message)),
        [typeof(ArgumentException)] = (ex, _) =>
        {
            var argument = (ArgumentException)ex;
            return new(ErrorCategories.InvalidArgument,
                BuildSafeArgumentMessage(argument),
                ParamName: argument.ParamName);
        },
        [typeof(TimeoutException)] = (_, _) => new(ErrorCategories.Timeout,
            "The operation timed out. For build/test operations, increase ROSLYNMCP_BUILD_TIMEOUT_SECONDS or " +
            "ROSLYNMCP_TEST_TIMEOUT_SECONDS. For other operations, increase ROSLYNMCP_REQUEST_TIMEOUT_SECONDS."),
        // preview-token-stale-across-auto-reload: a *_apply call whose paired preview was
        // invalidated by an auto-reload version bump (InvalidateOnVersionBump) or TTL
        // expiry surfaces as PreviewTokenStaleException instead of the legacy bare
        // KeyNotFoundException. PreviewTokenStaleException derives from
        // InvalidOperationException so this entry MUST register BEFORE the generic
        // InvalidOperationException handler below — the dictionary walk's
        // Type.IsAssignableFrom check uses insertion order, so a later InvalidOperation
        // entry would otherwise win and surface the less-specific category. Recovery hint
        // mirrors WorkspaceEvicted: structured, copy-pasteable signal that the workspace
        // is intact and the client just needs to re-issue the paired *_preview call.
        [typeof(PreviewTokenStaleException)] = (ex, _) =>
        {
            return new(ErrorCategories.PreviewTokenStale,
                "The preview token has expired because the workspace was reloaded after the preview was created. " +
                "Re-issue the paired *_preview call.");
        },
        // compile-check-not-connected-raw-transport-error-envelope: a disconnected stdio
        // pipe surfaces as InvalidOperationException("Not connected") from PipeStream when
        // the SDK attempts a write on an already-closed transport. Registered BEFORE the
        // generic InvalidOperationException entry so the Disconnected category wins over
        // the fallback InvalidOperation envelope. Recovery hint mirrors WorkspaceEvicted:
        // the workspace itself is intact — the client needs to reconnect and reload.
        [typeof(InvalidOperationException)] = (ex, _) =>
        {
            if (ex.Message.Contains("Not connected", StringComparison.OrdinalIgnoreCase))
            {
                return new(ErrorCategories.Disconnected,
                    "The stdio transport disconnected because the MCP client closed the pipe. " +
                    "Reconnect and call workspace_reload to restore the session.");
            }

            return ex.Message.Contains("Rate limit")
                ? new(ErrorCategories.RateLimited, "The request rate limit was exceeded. Wait for the current rate-limit window to reset, then retry.")
                : new(ErrorCategories.InvalidOperation,
                    ShouldSuggestReloadAfterInvalidOperation(ex.Message)
                        ? "The operation is not valid for the current workspace state. Call workspace_reload if the state is stale, then retry."
                        : "The operation is not valid in the current state. Check the tool contract and retry.");
        },
    };

    // mcp-parameter-validation-error-messages: the five known parameter-binding exception
    // shapes, dispatched by an insertion-order IsAssignableFrom walk (mirrors _errorHandlers).
    // Order MUST stay JsonException, ArgumentNullException, ArgumentOutOfRangeException,
    // ArgumentException, FormatException so the two ArgumentException-derived types match
    // before their base — exactly reproducing the former switch-arm precedence. Each lambda
    // casts to the typed exception internally to preserve ParamName extraction.
    private static readonly Dictionary<Type, Func<Exception, ErrorInfo>> _bindingLikeHandlers = new()
    {
        [typeof(System.Text.Json.JsonException)] = _ => new(ErrorCategories.InvalidArgument,
            "Parameter binding failed during JSON deserialization. " +
            "Check that JSON property names match the tool schema exactly (camelCase)."),
        [typeof(ArgumentNullException)] = ex =>
        {
            var nullArgEx = (ArgumentNullException)ex;
            return new(ErrorCategories.InvalidArgument,
                $"Required parameter '{nullArgEx.ParamName ?? "<unknown>"}' is missing or null. " +
                "Provide a value for this parameter and retry.",
                ParamName: nullArgEx.ParamName);
        },
        [typeof(ArgumentOutOfRangeException)] = ex =>
        {
            var rangeEx = (ArgumentOutOfRangeException)ex;
            return new(ErrorCategories.InvalidArgument,
                BuildSafeArgumentMessage(rangeEx),
                ParamName: rangeEx.ParamName);
        },
        [typeof(ArgumentException)] = ex =>
        {
            var argEx = (ArgumentException)ex;
            return new(ErrorCategories.InvalidArgument,
                BuildSafeArgumentMessage(argEx),
                ParamName: argEx.ParamName);
        },
        [typeof(FormatException)] = _ => new(ErrorCategories.InvalidArgument,
            "A parameter value has an invalid format. " +
            "Check that values match the expected format (e.g., integers, booleans, paths)."),
    };

    /// <summary>
    /// Success-only scope for resource handlers: failures travel over the JSON-RPC error
    /// channel. Keeps the ambient gate-metrics scope and success-path <c>_meta</c> injection,
    /// but has NO catch arm — every exception propagates to <c>ResourceReadResultFilter</c>,
    /// which translates it into a protocol error (<c>McpProtocolException</c>) instead of a
    /// serialized error document in the resource body. Elapsed time is stamped on the success
    /// path only: on failure the ambient <c>AmbientGateMetrics</c> scope disposes before the
    /// read filter observes the exception and nothing reads the AsyncLocal snapshot again, so
    /// a failure-path stamp would be write-only.
    /// </summary>
    public static string ExecuteResourceScope(string source, Func<string> action)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        using var metricsScope = AmbientGateMetrics.BeginRequest();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = action();
        // Stamp elapsed BEFORE _meta injection so the success snapshot carries it.
        RecordElapsed(stopwatch);
        return InjectMetaIfPossible(result, source);
    }

    /// <summary>
    /// Async variant of <see cref="ExecuteResourceScope"/> for awaitable resource handlers.
    /// </summary>
    public static async Task<string> ExecuteResourceScopeAsync(string source, Func<Task<string>> action)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        using var metricsScope = AmbientGateMetrics.BeginRequest();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await action().ConfigureAwait(false);
        RecordElapsed(stopwatch);
        return InjectMetaIfPossible(result, source);
    }

    private static void RecordElapsed(System.Diagnostics.Stopwatch stopwatch)
    {
        stopwatch.Stop();
        if (AmbientGateMetrics.Current is { } metrics)
        {
            metrics.ElapsedMs = stopwatch.ElapsedMilliseconds;
        }
    }

    /// <summary>
    /// P3: Parse the JSON result and add a top-level <c>_meta</c> property carrying the gate
    /// metrics snapshot. Only injected when the root is a JSON object — non-object roots
    /// (arrays, primitives) are returned unchanged so we don't break consumers that expect a
    /// specific shape. On any parse failure the original string is returned unchanged so this
    /// never breaks a well-formed response. Exposed <c>internal</c> so the
    /// <c>StructuredCallToolFilter</c> can inject <c>_meta</c> on both success and error paths.
    /// </summary>
    internal static string InjectMetaIfPossible(string json, string? source = null)
    {
        var snapshot = AmbientGateMetrics.Snapshot();
        if (snapshot is null)
        {
            System.Diagnostics.Trace.TraceWarning(
                "_meta injection skipped for {0}: no ambient metrics scope", source ?? "unknown");
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "_meta injection skipped for {0}: response root is not a JSON object", source ?? "unknown");
                return json;
            }

            var metaNode = JsonSerializer.SerializeToNode(snapshot, MetaSerializer.CamelCase);
            if (metaNode is null) return json;

            obj["_meta"] = metaNode;
            return obj.ToJsonString(JsonDefaults.Indented);
        }
        catch
        {
            // Best-effort: never break a tool response over an injection failure.
            return json;
        }
    }

    internal static ErrorInfo ClassifyError(Exception ex, string toolName, string? correlationId = null)
    {
        // mcp-parameter-validation-error-messages: detect parameter-binding failures
        // in BOTH shapes the runtime can deliver them:
        //   1. Wrapped in a generic invocation envelope — legacy reflection dispatch surfaces
        //      parameter problems as InvalidOperationException or TargetInvocationException
        //      with the real cause as ImmediateInnerException. The dictionary entry for
        //      InvalidOperationException would otherwise mask this as a generic "InvalidOperation".
        //   2. Raw (unwrapped) — once wired through ModelContextProtocol's AddCallToolFilter
        //      (SDK PR csharp-sdk#844, shipped in 0.4.0-preview.3 and carried into 1.x), the
        //      filter pipeline propagates pre-binding ArgumentException/JsonException/etc.
        //      directly without an invocation wrapper.
        //
        // Both shapes produce the same structured InvalidArgument envelope. The helper
        // TryClassifyBindingLike runs against the inner exception when there's an invocation
        // wrapper, otherwise against the exception itself. Legitimate internal errors
        // (AggregateException → InvalidCastException → ArgumentException buried deep in
        // domain code) are NOT misclassified because the shallow walk only inspects the
        // immediate exception at each level.
        if (IsInvocationWrapper(ex) && ex.InnerException is { } directInner)
        {
            if (TryClassifyBindingLike(directInner, out var wrappedInfo))
                return wrappedInfo;
        }
        else if (TryClassifyBindingLike(ex, out var rawInfo))
        {
            return rawInfo;
        }

        if (TryClassifyReloadRace(ex, out var reloadRaceInfo))
            return reloadRaceInfo;

        if (TryClassifyRegisteredHandler(ex, toolName, out var registeredInfo))
            return registeredInfo;

        return BuildUnexpectedErrorFallback(ex, correlationId ?? RequestCorrelationContext.Current);
    }

    /// <summary>
    /// Classifies a mid-call auto-reload race: a <see cref="KeyNotFoundException"/> raised while
    /// the request's gate reported it had to auto-reload the workspace, surfacing a race-specific
    /// envelope instead of the generic "NotFound" that implies the symbol itself is gone. Returns
    /// <see langword="false"/> for every other exception so the caller falls through to the
    /// dictionary walk.
    /// </summary>
    private static bool TryClassifyReloadRace(Exception ex, out ErrorInfo info)
    {
        // symbol-impact-sweep-race-with-auto-reload: when a symbol fails to resolve AND the
        // request's gate reported it had to auto-reload the workspace mid-call, surface a
        // race-specific envelope instead of the generic "NotFound" that implies the symbol
        // itself is gone. The pre-reload handle/metadata-name may still be valid; callers
        // just need to re-resolve against the fresh compilation and retry. Scope is
        // deliberately narrow — any KeyNotFoundException outside a reload window still
        // falls through to the generic NotFound handler.
        //
        // mcp-error-category-workspace-evicted-on-host-recycle: WorkspaceEvictedException
        // derives from KeyNotFoundException for catch-site compatibility, so the generic
        // `is KeyNotFoundException` test would otherwise re-classify a recycle-eviction
        // as WorkspaceReloadedDuringCall whenever the in-call gate reported a stale auto-
        // reload. Exclude the more-specific eviction type here so the dictionary handler
        // (registered with explicit `typeof(WorkspaceEvictedException)`) wins.
        //
        // workspace-reloaded-during-call-conflates-notfound: when the gate retried after
        // the auto-reload and the second attempt also failed with "Document not found", the
        // gate stamps ReloadConfirmedNotFound=true. That confirms the file path is genuinely
        // absent — not a stale-snapshot race — so fall through to the generic NotFound handler
        // rather than emitting the misleading WorkspaceReloadedDuringCall category.
        if (ex is KeyNotFoundException && ex is not WorkspaceEvictedException &&
            AmbientGateMetrics.Current?.StaleAction == "auto-reloaded" &&
            AmbientGateMetrics.Current?.ReloadConfirmedNotFound != true)
        {
            info = new(ErrorCategories.WorkspaceReloadedDuringCall,
                "The workspace was auto-reloaded during this call. The symbol handle " +
                "or metadata name may target the pre-reload compilation. Re-resolve the symbol " +
                "(e.g. via symbol_search, symbol_info, or a fresh position-based locator) and retry. " +
                "See _meta.staleReloadMs for how long the reload held the request.");
            return true;
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Walks the <see cref="_errorHandlers"/> dictionary in insertion order and returns the
    /// first entry whose registered type is assignable from <paramref name="ex"/>'s runtime
    /// type. Insertion order encodes precedence (more-specific types registered first), so this
    /// must stay a linear walk rather than an exact-type lookup. Returns <see langword="false"/>
    /// when no registered handler matches.
    /// </summary>
    private static bool TryClassifyRegisteredHandler(Exception ex, string toolName, out ErrorInfo info)
    {
        foreach (var (type, handler) in _errorHandlers)
        {
            if (type.IsAssignableFrom(ex.GetType()))
            {
                info = handler(ex, toolName);
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Builds the terminal <c>InternalError</c> envelope for an unexpected exception. Raw
    /// exception detail stays server-side; the client receives only a stable recovery shape.
    /// </summary>
    private static ErrorInfo BuildUnexpectedErrorFallback(Exception ex, string? correlationId)
    {
        var publicDetail = PublicExceptionDetailPolicy
            .ProjectUnexpected(ex, correlationId)
            .Public;
        return new(
            publicDetail.Category,
            $"{publicDetail.Summary} {publicDetail.Remediation}",
            CorrelationId: publicDetail.CorrelationId);
    }

    /// <summary>
    /// Classifies <paramref name="ex"/> against the five known parameter-binding exception
    /// shapes (<see cref="System.Text.Json.JsonException"/>, <see cref="ArgumentNullException"/>,
    /// <see cref="ArgumentOutOfRangeException"/>, <see cref="ArgumentException"/>,
    /// <see cref="FormatException"/>) and produces an <c>InvalidArgument</c> envelope that
    /// names the offending parameter where possible. Returns <see langword="false"/> when
    /// the exception is not a binding-like shape, leaving the caller to fall through to
    /// the dictionary or the default <c>InternalError</c> branch.
    /// </summary>
    private static bool TryClassifyBindingLike(Exception ex, out ErrorInfo info)
    {
        foreach (var (type, handler) in _bindingLikeHandlers)
        {
            if (type.IsAssignableFrom(ex.GetType()))
            {
                info = handler(ex);
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>
    /// Facade for the StructuredCallToolFilter: classify an exception and return a fully
    /// formatted JSON error envelope (without <c>_meta</c>; inject that separately). Wraps
    /// <see cref="ClassifyError"/> + <see cref="FormatErrorResponse"/> so callers don't need
    /// access to the private <see cref="ErrorInfo"/> type.
    /// </summary>
    internal static string ClassifyAndFormat(Exception ex, string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        var info = ClassifyError(ex, toolName);
        return FormatErrorResponse(info, toolName, ex);
    }

    /// <summary>
    /// Adds a catalog-backed <c>schemaHint</c> field to an existing JSON error envelope when
    /// a tool-specific catch path wants retry-shape guidance without changing the global
    /// classifier contract. Unknown tools and non-object payloads are returned unchanged.
    /// </summary>
    internal static string InjectSchemaHintIfPossible(string json, string toolName, string? paramName = null)
    {
        var schemaHint = BuildSchemaHint(toolName, paramName);
        if (schemaHint is null) return json;

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                return json;
            }

            obj["schemaHint"] = schemaHint;
            return obj.ToJsonString(JsonDefaults.Indented);
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// Returns true when the exception type indicates a generic SDK/reflection invocation
    /// wrapper that hides the real cause in InnerException. Used by the parameter-binding
    /// detector to scope its inner-exception check to legitimate wrappers (and avoid
    /// misclassifying domain errors that happen to have an Argument-like exception in their
    /// chain).
    /// </summary>
    private static bool IsInvocationWrapper(Exception ex)
    {
        if (ex is System.Reflection.TargetInvocationException) return true;
        // The MCP SDK + Microsoft.Extensions.* hosting wrap with InvalidOperationException
        // when a tool method throws during binding. Walk type names rather than concrete
        // types so the SDK can rev internal exception classes without breaking us.
        var name = ex.GetType().Name;
        return name.Contains("Invocation", StringComparison.OrdinalIgnoreCase)
            || (name == nameof(InvalidOperationException) && ex.Message.Contains("invocation", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Only suggest workspace_reload when the message plausibly indicates stale or missing workspace state.
    /// </summary>
    private static bool ShouldSuggestReloadAfterInvalidOperation(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found in workspace", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Document not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("workspace changed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("workspace version", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("outside the workspace", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("preview token", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSafeNotFoundMessage(string rawMessage)
    {
        if (rawMessage.StartsWith("No identifier at this position", StringComparison.Ordinal))
        {
            return "No identifier at this position. Move the source location onto a symbol name and retry.";
        }

        if (rawMessage.Contains("metadata-only", StringComparison.OrdinalIgnoreCase))
        {
            return "The resolved symbol is metadata-only and has no source definition.";
        }

        return "The requested item was not found. Ensure the workspace is loaded and the identifier is correct.";
    }

    private static string BuildSafeArgumentMessage(ArgumentException exception)
    {
        var parameter = exception.ParamName ?? "<unknown>";
        var rawMessage = exception.Message;

        if (exception is PromptParameterBindingException promptBinding)
        {
            return promptBinding.PublicMessage;
        }

        if (string.Equals(exception.ParamName, "parametersJson", StringComparison.Ordinal))
        {
            return "Parameter 'parametersJson' must be a JSON object whose properties match the prompt schema. " +
                "Include every required prompt parameter and use values of the advertised types.";
        }

        if (string.Equals(exception.ParamName, "projectName", StringComparison.Ordinal))
        {
            return "No loaded project matches parameter 'projectName'. Use workspace_status to list project names, then retry.";
        }

        if (string.Equals(exception.ParamName, "lineRange", StringComparison.Ordinal))
        {
            return rawMessage.Contains("past the end", StringComparison.OrdinalIgnoreCase)
                ? "Parameter 'lineRange' starts past the end of the source file. Request a range within the reported line count."
                : "Parameter 'lineRange' must use the 1-based 'startLine-endLine' format with startLine less than or equal to endLine.";
        }

        if (string.Equals(exception.ParamName, "startLine", StringComparison.Ordinal) &&
            rawMessage.Contains("must be <=", StringComparison.Ordinal))
        {
            return "Parameter 'startLine' must be less than or equal to endLine.";
        }

        if (string.Equals(exception.ParamName, "workspaceId", StringComparison.Ordinal) &&
            rawMessage.Contains("candidate solution", StringComparison.OrdinalIgnoreCase))
        {
            return "Workspace auto-discovery found multiple candidates. Call workspace_load with an explicit solution or project path.";
        }

        // semantic-grep-pattern-error-detail-redaction: SemanticGrepService throws a fixed
        // input-free sentinel when the caller's regex fails to parse (the raw .NET parser
        // message embeds the submitted pattern, which may itself be a secret). Guard on BOTH
        // ParamName and the sentinel substring so other tools' 'pattern' parameters (e.g.
        // RestructureService's non-empty check) keep the generic fallback below.
        if (string.Equals(exception.ParamName, "pattern", StringComparison.Ordinal) &&
            rawMessage.Contains("not a valid .NET regular expression", StringComparison.Ordinal))
        {
            return "Parameter 'pattern' is not a valid .NET regular expression. Patterns use " +
                "System.Text.RegularExpressions syntax, not ripgrep/PCRE. Check for unbalanced " +
                "parentheses or brackets, invalid quantifier ranges, and unescaped metacharacters, " +
                "then retry with a corrected pattern.";
        }

        if (rawMessage.Contains("Unsupported catalog diff", StringComparison.Ordinal))
        {
            return "Unsupported catalog diff. Request one of the version pairs advertised by the catalog resource.";
        }

        if (rawMessage.Contains("filePath", StringComparison.OrdinalIgnoreCase) &&
            rawMessage.Contains("column", StringComparison.OrdinalIgnoreCase))
        {
            return "Source-location mode is incomplete; provide filePath, line, and column together. " +
                "Alternatively supply symbolHandle or metadataName.";
        }

        return $"Parameter '{parameter}' is invalid. Check that all required parameters are provided and values match the expected types.";
    }

    /// <summary>
    /// tool-error-handler-envelope-duplication: single constructor for the JSON error envelope.
    /// Emits the five base properties (<c>error</c>, <c>category</c>, <c>tool</c>, <c>message</c>,
    /// <c>exceptionType</c>) in a fixed order, then optionally one structured extra field appended
    /// last — the same shape the per-branch anonymous-object literals used to produce. Adding a new
    /// structured error field is a call-site argument, not a sixth copy of the envelope.
    /// </summary>
    /// <param name="extraFieldName">
    /// camelCase key for the extra field, or <see langword="null"/> to emit the bare envelope. The
    /// key is written verbatim — <see cref="JsonDefaults.Indented"/>'s naming policy applies only to
    /// the serialized <paramref name="extraFieldValue"/>, never to <see cref="JsonObject"/> keys.
    /// </param>
    /// <param name="extraFieldValue">
    /// Value for the extra field, serialized with the shared options so nested records/lists keep the
    /// camelCase + enum-as-string shape. Declared <see cref="object"/> so the runtime type drives
    /// serialization (e.g. <c>List&lt;T&gt;</c> extras stay arrays of objects).
    /// </param>
    private static string BuildErrorEnvelope(
        ErrorInfo info,
        string toolName,
        Exception ex,
        string? extraFieldName = null,
        object? extraFieldValue = null)
    {
        var envelope = new JsonObject
        {
            ["error"] = true,
            ["category"] = info.Category,
            ["tool"] = toolName,
            ["message"] = info.Message,
            ["exceptionType"] = ex.GetType().Name,
        };

        if (extraFieldName is not null)
        {
            envelope[extraFieldName] = JsonSerializer.SerializeToNode(extraFieldValue, JsonDefaults.Indented);
        }

        return envelope.ToJsonString(JsonDefaults.Indented);
    }

    internal static string FormatErrorResponse(ErrorInfo info, string toolName, Exception ex)
    {
        if (info.Category == ErrorCategories.InternalError)
        {
            return new JsonObject
            {
                ["error"] = true,
                ["category"] = info.Category,
                ["tool"] = toolName,
                ["message"] = info.Message,
                ["correlationId"] = info.CorrelationId ?? "unavailable",
            }.ToJsonString(JsonDefaults.Indented);
        }

        if (ex is SymbolNotFoundException { ClosestMatches.Count: > 0 } symbolNotFound)
        {
            return BuildErrorEnvelope(info, toolName, ex, "closestMatches", symbolNotFound.ClosestMatches);
        }

        // extract-type-preview-refusal-missing-blocking-deps: mirror the closestMatches
        // treatment above for extract_type_preview's refusals — the prose message and the
        // InvalidOperation category are unchanged; the envelope just gains the structured
        // per-member blocking data the refusal was computed from.
        if (ex is ExtractTypeBlockingDependencyException { BlockingDependencies.Count: > 0 } blockedExtraction)
        {
            return BuildErrorEnvelope(info, toolName, ex, "blockingDependencies", blockedExtraction.BlockingDependencies);
        }

        // inv-arg-envelope-schema-hint: attach a one-line schema hint for InvalidArgument
        // envelopes so cold-context callers can re-call without round-tripping through
        // server_info. Hint is omitted (rather than emitted as null) when the failing
        // parameter cannot be resolved against the tool catalog — keeps the envelope
        // shape stable for downstream parsers that do not expect the field.
        var schemaHint = info.Category == ErrorCategories.InvalidArgument
            ? BuildSchemaHint(toolName, info.ParamName)
            : null;

        return schemaHint is not null
            ? BuildErrorEnvelope(info, toolName, ex, "schemaHint", schemaHint)
            : BuildErrorEnvelope(info, toolName, ex);
    }

    /// <summary>
    /// inv-arg-envelope-schema-hint: format a one-line schema hint sourced from the live
    /// tool catalog. Returns the matching parameter's signature when <paramref name="paramName"/>
    /// is known, the full tool signature when only the tool is known, or <see langword="null"/>
    /// when the tool itself is not in the index (e.g. resource calls). Description text is
    /// trimmed to the first sentence to keep the hint compact.
    /// </summary>
    private static string? BuildSchemaHint(string toolName, string? paramName)
    {
        if (!string.IsNullOrEmpty(paramName))
        {
            var schema = ToolParameterIndex.GetParameter(toolName, paramName);
            if (schema is not null) return FormatParameter(toolName, schema);
        }

        var all = ToolParameterIndex.GetParameters(toolName);
        if (all.Count == 0) return null;

        var formatted = string.Join(", ", all.Select(p => $"{p.Name}: {FormatTypeWithOptionalMarker(p)}"));
        return $"{toolName}({formatted})";
    }

    private static string FormatParameter(string toolName, ToolParameterSchema schema)
    {
        var description = string.IsNullOrEmpty(schema.Description)
            ? string.Empty
            : $" — {TrimToFirstSentence(schema.Description)}";
        return $"{toolName}({schema.Name}: {FormatTypeWithOptionalMarker(schema)}{description})";
    }

    private static string FormatTypeWithOptionalMarker(ToolParameterSchema schema)
    {
        if (schema.Required || schema.Type.EndsWith("?", StringComparison.Ordinal))
        {
            return schema.Type;
        }

        return schema.Type + "?";
    }

    private static string TrimToFirstSentence(string text)
    {
        // Tool descriptions often span multiple sentences (intent + caveats). Hint stays
        // useful when only the first sentence ships; longer text bloats the envelope without
        // proportional information gain. Cap at 160 chars even for single-sentence text so
        // multi-kilobyte descriptions can't leak verbatim.
        var period = text.IndexOf(". ", StringComparison.Ordinal);
        var firstSentence = period > 0 ? text[..period] : text;
        return firstSentence.Length > 160 ? firstSentence[..160] + "…" : firstSentence;
    }

    internal readonly record struct ErrorInfo(
        string Category,
        string Message,
        string? ParamName = null,
        string? CorrelationId = null);
}

/// <summary>
/// Exception type for tool errors. Can be used by resource handlers and other non-tool contexts
/// where structured JSON error responses are not applicable. The optional
/// <see cref="OriginatingSource"/> property carries the originating tool name or resource URI
/// so the MCP framework can label error envelopes with the actual originator instead of
/// "unknown".
/// </summary>
public sealed class McpToolException : Exception
{
    /// <summary>
    /// Originating tool name or resource URI (e.g., <c>"workspace_status"</c>,
    /// <c>"roslyn://workspaces"</c>). When null, defaults to "unknown" in the rendered envelope.
    /// Named with the <c>Originating</c> prefix to avoid colliding with the inherited
    /// <see cref="Exception.Source"/> property (which is the assembly name by default).
    /// </summary>
    public string? OriginatingSource { get; }

    public McpToolException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    public McpToolException(string source, string message, Exception? inner = null) : base(message, inner)
    {
        OriginatingSource = source;
    }
}
