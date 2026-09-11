using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Elicitation;
using RoslynMcp.Host.Stdio.ProtocolCompatibility;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// Resolves an omitted <c>workspaceId</c> before the SDK binder runs. This owner only decides
/// workspace identity and auto-load behavior; request-scoped elicitation/retry belongs to
/// <see cref="StructuredDispatchPipeline"/>.
/// </summary>
internal static class StructuredWorkspaceResolver
{
    /// <summary>The outcome of pre-dispatch workspace identity resolution.</summary>
    internal enum WorkspaceIdAutoResolution
    {
        NotApplicable,
        Explicit,
        SingleWorkspace,
        FilePathWorkspace,
        FastFail,
    }

    /// <summary>
    /// A terminal exception is formatted only after elapsed metrics are recorded. <see cref="ShouldTryPathRecovery"/>
    /// is true only after a zero-workspace discovery path has not identified an identity.
    /// </summary>
    internal readonly record struct ResolutionOutcome(
        Exception? TerminalException,
        bool ShouldTryPathRecovery);

    internal static bool IsApplicable(
        RequestContext<CallToolRequestParams> context,
        string toolName) =>
        context.Services?.GetService<IWorkspaceManager>() is not null &&
        ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(toolName);

    internal static bool HasAuthoritativeWorkspacePathResponse(
        RequestContext<CallToolRequestParams> context) =>
        RequestProtocolFeatureGate.SupportsJuly2026Features(context) &&
        ElicitationChoicePrompt.SupportsElicitation(context) &&
        context.Params?.InputResponses?.ContainsKey(
            RequestScopedInputAdapter.WorkspacePathInputRequestKey) is true;

    internal static async Task<ResolutionOutcome> ResolveAsync(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        ILogger? logger,
        CancellationToken cancellationToken,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> invokeRegisteredToolAsync)
    {
        var workspaceManager = context.Services?.GetService<IWorkspaceManager>();
        if (workspaceManager is null ||
            !ElicitationAllowlistPolicy.IsWorkspaceIdAutoResolveAllowedFor(toolName))
        {
            return default;
        }

        var workspaceIdMissingOrBlank = IsWorkspaceIdMissingOrBlank(context.Params?.Arguments);
        var restoredWorkspaceFromRequestState = false;
        if (workspaceIdMissingOrBlank &&
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

        if (!IsWorkspaceIdMissingOrBlank(context.Params?.Arguments))
        {
            // Explicit ids are intentionally left untouched. The common path avoids per-call
            // workspace projection, while request-state restoration gets its own metric above.
            if (!restoredWorkspaceFromRequestState)
            {
                CallMetricsRecorder.RecordAutoResolution("explicit");
            }

            return default;
        }

        var loadedWorkspaces = workspaceManager.ListWorkspaces()
            .Select(WorkspaceStatusSummaryDto.From)
            .ToArray();
        var filePathOwnerIds = TryGetStringArgument(context.Params?.Arguments, "filePath") is { } filePath
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
                context.Params!.Arguments = WithWorkspaceId(context.Params.Arguments, resolvedWorkspaceId!);
                CallMetricsRecorder.RecordAutoResolution("single-workspace");
                logger?.LogInformation(
                    "Tool {ToolName} called without workspaceId; resolved to the single loaded workspace {WorkspaceId}.",
                    toolName,
                    resolvedWorkspaceId);
                return default;

            case WorkspaceIdAutoResolution.FilePathWorkspace:
                context.Params!.Arguments = WithWorkspaceId(context.Params.Arguments, resolvedWorkspaceId!);
                CallMetricsRecorder.RecordAutoResolution("file-path");
                logger?.LogInformation(
                    "Tool {ToolName} called without workspaceId; resolved filePath to workspace {WorkspaceId}.",
                    toolName,
                    resolvedWorkspaceId);
                return default;

            case WorkspaceIdAutoResolution.FastFail:
                CallMetricsRecorder.RecordAutoResolution("fast-fail");
                logger?.LogWarning(
                    "Tool {ToolName} called without workspaceId while {Count} workspaces are loaded; returning a structured fast-fail.",
                    toolName,
                    loadedWorkspaces.Length);
                return new ResolutionOutcome(
                    new PublicArgumentException(
                        fastFailMessage!,
                        ElicitationAllowlistPolicy.WorkspaceIdParameterName),
                    ShouldTryPathRecovery: false);

            case WorkspaceIdAutoResolution.NotApplicable:
                {
                    var autoLoadFastFail = await TryAutoLoadWorkspaceAsync(
                        context,
                        toolName,
                        logger,
                        cancellationToken,
                        invokeRegisteredToolAsync).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return autoLoadFastFail is null
                        ? new ResolutionOutcome(TerminalException: null, ShouldTryPathRecovery: true)
                        : new ResolutionOutcome(autoLoadFastFail, ShouldTryPathRecovery: false);
                }

            case WorkspaceIdAutoResolution.Explicit:
                // Explicit input is short-circuited above. Keep this defensive branch silent.
                return default;

            default:
                throw new InvalidOperationException($"Unknown workspace resolution result '{resolution}'.");
        }
    }

    /// <summary>
    /// Classifies an auto-resolution-eligible call using explicit id, a single loaded workspace,
    /// a unique file owner, or a bounded candidate fast-fail.
    /// </summary>
    internal static WorkspaceIdAutoResolution ClassifyWorkspaceIdResolution(
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

    internal static WorkspaceIdAutoResolution ClassifyWorkspaceIdResolution(
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

        if (loadedWorkspaces.Count < 2)
        {
            return WorkspaceIdAutoResolution.NotApplicable;
        }

        var ownerIds = filePathOwnerIds.ToHashSet(StringComparer.Ordinal);
        if (ownerIds.Count == 1)
        {
            resolvedWorkspaceId = ownerIds.Single();
            return WorkspaceIdAutoResolution.FilePathWorkspace;
        }

        var candidates = ownerIds.Count > 1
            ? loadedWorkspaces.Where(workspace => ownerIds.Contains(workspace.WorkspaceId)).ToArray()
            : loadedWorkspaces;
        fastFailMessage =
            "workspaceId was omitted and filePath did not identify one loaded workspace. " +
            $"Candidates: {FormatWorkspaceChoices(candidates)}. Pass workspaceId explicitly; " +
            "call workspace_list to refresh choices.";
        return WorkspaceIdAutoResolution.FastFail;
    }

    internal static bool IsWorkspaceIdMissingOrBlank(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null ||
            !arguments.TryGetValue(ElicitationAllowlistPolicy.WorkspaceIdParameterName, out var value))
        {
            return true;
        }

        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
               (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));
    }

    internal static IDictionary<string, JsonElement> WithWorkspaceId(
        IDictionary<string, JsonElement>? existing,
        string workspaceId)
    {
        var arguments = existing is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(existing, StringComparer.Ordinal);
        arguments[ElicitationAllowlistPolicy.WorkspaceIdParameterName] =
            JsonSerializer.SerializeToElement(workspaceId);
        return arguments;
    }

    /// <summary>
    /// Rechecks cancellation after an awaited recovery stage before the returned value can drive
    /// argument mutation or another dispatch.
    /// </summary>
    internal static async Task<T> AwaitRecoveryStageAsync<T>(
        Task<T> stage,
        CancellationToken cancellationToken)
    {
        var result = await stage.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<Exception?> TryAutoLoadWorkspaceAsync(
        RequestContext<CallToolRequestParams> context,
        string toolName,
        ILogger? logger,
        CancellationToken cancellationToken,
        Func<string, IReadOnlyDictionary<string, JsonElement>, Task<CallToolResult>> invokeRegisteredToolAsync)
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
                        invokeRegisteredToolAsync(ElicitationAllowlistPolicy.WorkspaceLoadToolName, loadArguments),
                        cancellationToken).ConfigureAwait(false);
                    var workspaceId = StructuredCallElicitationCoordinator.TryExtractWorkspaceId(loadResult);
                    stopwatch.Stop();

                    if (string.IsNullOrWhiteSpace(workspaceId))
                    {
                        logger?.LogWarning(
                            "Auto-load discovered {Path} for {Tool} but workspace_load returned no id; falling back to the recovery path.",
                            discovery.UniquePath,
                            toolName);
                        return null;
                    }

                    context.Params!.Arguments = WithWorkspaceId(context.Params.Arguments, workspaceId);
                    CallMetricsRecorder.RecordAutoResolution("auto-loaded");
                    CallMetricsRecorder.RecordAutoLoadElapsed(stopwatch.ElapsedMilliseconds);
                    logger?.LogInformation(
                        "Tool {ToolName} called without workspaceId and none loaded; auto-loaded {Path} as {WorkspaceId} in {ElapsedMs}ms.",
                        toolName,
                        discovery.UniquePath,
                        workspaceId,
                        stopwatch.ElapsedMilliseconds);
                    return null;
                }

            case SolutionDiscoveryHelper.DiscoveryStatus.Ambiguous:
                {
                    CallMetricsRecorder.RecordAutoResolution("fast-fail");
                    var candidates = string.Join(", ", discovery.Candidates);
                    logger?.LogWarning(
                        "Tool {ToolName} called without workspaceId and none loaded; {Count} candidate solutions discovered ({Candidates}).",
                        toolName,
                        discovery.Candidates.Count,
                        candidates);
                    return new PublicArgumentException(
                        $"workspaceId was omitted and no workspace is loaded. {discovery.Candidates.Count} " +
                        $"candidate solutions were discovered ({candidates}). Call workspace_load(path=…) with " +
                        "one of them, then retry — or pass workspaceId explicitly.",
                        ElicitationAllowlistPolicy.WorkspaceIdParameterName);
                }

            case SolutionDiscoveryHelper.DiscoveryStatus.None:
            default:
                return null;
        }
    }

    private static string FormatWorkspaceChoices(IEnumerable<WorkspaceStatusSummaryDto> workspaces)
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

    private static string? TryGetStringArgument(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null &&
        arguments.TryGetValue(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
