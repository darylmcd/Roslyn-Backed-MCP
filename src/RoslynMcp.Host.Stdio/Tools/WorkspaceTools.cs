using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private const int AutoPrewarmProjectThreshold = 50;
    private const int DefaultSupportBundleChangeCap = 20;
    private const int MaxSupportBundleChangeCap = 50;
    private const int DefaultSupportBundleDriftCap = 25;
    private const int MaxSupportBundleDriftCap = 100;
    private const string ReadinessVerdictReady = "ready";
    private const string ReadinessVerdictRestoreNeeded = "restore-needed";
    private const string ReadinessVerdictBuildNeeded = "build-needed";
    private const string ReadinessVerdictAnalyzerLimited = "analyzer-limited";

    [McpServerTool(Name = "workspace_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description("Load a .sln, .slnx, or .csproj file into the workspace for semantic analysis. Returns a lean summary by default — pass verbose=true for the full per-project tree (large solutions can produce ~30 KB or more). Idempotent by path: if the same solution/project file is already loaded in this host process, workspace_load returns the EXISTING WorkspaceId instead of creating a new one — no extra workspace slot is consumed. Set autoRestore=true to run dotnet restore and one follow-up reload when the loaded status reports restoreRequired=true. Set prewarm=true to immediately run the workspace_warm compilation/semantic-model prewarm after a successful load or auto-restore reload; set prewarm=false to opt out. When prewarm is omitted, workspace_load automatically prewarms solutions with more than 50 projects. The response includes a prewarm result block only when warming ran. DocumentCount note: the per-project DocumentCount often exceeds the <Compile> item count (from evaluate_msbuild_items) by about 3 because the SDK auto-generates implicit-usings, AssemblyInfo, and GlobalUsings files that Roslyn includes in the document set but MSBuild does not list as explicit <Compile> items. Sessions persist for the lifetime of the stdio host process — there is NO inactivity TTL. A workspace can become unreachable if (a) the host process restarts (Cursor/Claude Code may relaunch the MCP server transparently between conversations), (b) workspace_close is called, or (c) the concurrent-workspace cap (ROSLYNMCP_MAX_WORKSPACES, default 16) forced an eviction. When a previously valid workspaceId returns 'Workspace was not found', call workspace_load again rather than treating it as an error. Pass evictPolicy=lru to silently evict the least-recently-used idle workspace when the cap is reached instead of receiving a hard error.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Load a .sln, .slnx, or .csproj into a named Roslyn workspace session.")]
    public static Task<string> LoadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IWorkspaceWarmService warmService,
        IDotnetCommandRunner commandRunner,
        [Description("Absolute path to a .sln, .slnx, or .csproj file")] string path,
        [Description("When true, return the full per-project tree and workspace diagnostics. Default false returns only counts and load state.")] bool verbose = false,
        [Description("When true and the loaded status reports restoreRequired=true, run `dotnet restore` on the target and reload once before returning.")] bool autoRestore = false,
        [Description("When true, run `workspace_warm` immediately after the load (and any auto-restore reload) succeeds, then include the warm result in the response. When omitted, large solutions with more than 50 projects are prewarmed automatically. Pass false to opt out and preserve the cold-load profile.")] bool? prewarm = null,
        [Description("Operator-opt-in security flag (default false). When true, the client-sanctioned-root path validator additionally accepts paths under the immediate PARENT directory of each sanctioned root — enough to permit a sibling worktree at `../<name>` (e.g. mcp-server-surface-test's disposable audit worktree). Higher ancestors (grandparent etc.) are NOT widened. Pass true only from operator-controlled call sites; do not auto-enable on every request.")] bool expandSanctionedRoots = false,
        [Description("Controls cap-reached behaviour. 'Strict' (default) throws with activeWorkspaces and lruCandidate context for one-round-trip self-recovery. 'Lru' silently evicts the least-recently-used idle workspace to make room for the new load.")] EvictPolicy evictPolicy = EvictPolicy.Strict,
        IProgress<ProgressNotificationValue>? progress = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        var logger = CreateLogger(loggerFactory);
        return gate.RunLoadGateAsync(async c =>
        {
            // workspace-load stage emissions: clients see "validating-path → opening-workspace
            // → checking-restore → done" instead of waiting silently for a ~45s P95 cold load
            // on large solutions (OrchardCore, etc.). The stage labels are kebab-case and
            // stable; total is the stage count so client progress bars track correctly. The
            // opt-in or auto-large-solution prewarm path adds "prewarming-workspace" before
            // "done". For omitted prewarm, the project-count threshold can only be evaluated
            // after load returns, so the progress denominator may expand from 4 to 5 on
            // >50-project solutions.
            // Per-project N/M is intentionally not emitted here — IWorkspaceManager.LoadAsync
            // doesn't expose intra-load progress and adding it would balloon scope past the
            // audit-coverage initiative. See ProgressHelper remarks for the label-naming contract.
            var totalStages = prewarm == true ? 5 : 4;
            ProgressHelper.ReportStage(progress, 0, totalStages, "validating-path");
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(
                server, path, c, expandSanctionedRoots: expandSanctionedRoots).ConfigureAwait(false);
            ProgressHelper.ReportStage(progress, 1, totalStages, "opening-workspace");
            var status = await workspace.LoadAsync(path, evictPolicy, c).ConfigureAwait(false);
            ProgressHelper.ReportStage(progress, 2, totalStages, "checking-restore");
            status = await RestoreAndReloadIfRequiredAsync(commandRunner, workspace, status, autoRestore, c).ConfigureAwait(false);
            var shouldPrewarm = ShouldPrewarmAfterLoad(prewarm, status);
            var resolvedTotalStages = shouldPrewarm ? 5 : totalStages;
            WorkspaceWarmResult? prewarmResult = null;
            if (shouldPrewarm)
            {
                ProgressHelper.ReportStage(progress, 3, resolvedTotalStages, "prewarming-workspace");
                prewarmResult = await gate.RunReadAsync(
                    status.WorkspaceId,
                    warmCt => warmService.WarmAsync(status.WorkspaceId, projects: null, warmCt),
                    c).ConfigureAwait(false);
            }

            ProgressHelper.ReportStage(progress, resolvedTotalStages, resolvedTotalStages, "done");
            _ = NotifyResourcesChangedAsync(server, "workspace_load", logger);
            return SerializeWorkspaceLoadResult(status, verbose, prewarmResult);
        }, ct);
    }

    [McpServerTool(Name = "workspace_reload", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Reload the currently loaded workspace to pick up file changes. Set autoRestore=true to run dotnet restore and one follow-up reload when the reloaded status reports restoreRequired=true.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Reload an existing workspace session from disk.")]
    public static Task<string> ReloadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true and the reloaded status reports restoreRequired=true, run `dotnet restore` on the loaded target and reload once before returning.")] bool autoRestore = false,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        var logger = CreateLogger(loggerFactory);
        // Reload acquires both the global load gate AND the per-workspace write lock so that
        // any in-flight readers on this workspace complete before the solution is replaced.
        return gate.RunLoadGateAsync(outerCt =>
            gate.RunWriteAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.ReloadAsync(workspaceId, innerCt).ConfigureAwait(false);
                status = await RestoreAndReloadIfRequiredAsync(commandRunner, workspace, status, autoRestore, innerCt).ConfigureAwait(false);
                _ = NotifyResourcesChangedAsync(server, "workspace_reload", logger);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, outerCt), ct);
    }

    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources. Set drainProcesses=true to run `dotnet build-server shutdown` after session removal — this releases MSBuild build-server file-system locks on Windows, which is required before `git worktree remove` in sweep teardown sequences.")]
    [McpToolMetadata("workspace", "stable", false, true,
        "Close a loaded workspace session and release resources.")]
    public static Task<string> CloseWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, run `dotnet build-server shutdown` after session removal to release MSBuild out-of-process build-server locks. Default false. Set true before `git worktree remove` in sweep teardown.")] bool drainProcesses = false,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        var logger = CreateLogger(loggerFactory);
        // Close acquires both the global load gate AND the per-workspace write lock so that
        // no reader is in flight when the workspace's lock entry is dropped from the registry.
        // RemoveGate must run after RunWriteAsync completes so the per-workspace lock entry is
        // released before being removed from the registry.
        //
        // CAPTURE-BEFORE-CLOSE: workspace.Close(workspaceId) removes the session from the
        // internal registry. Resolve LoadedPath BEFORE calling Close so the working directory
        // for the drain step is available even after the session is gone.
        return gate.RunLoadGateAsync(async outerCt =>
        {
            string? loadedPath = null;
            var json = await gate.RunWriteAsync(
                workspaceId,
                async innerCt =>
                {
                    // Capture the loaded path before close removes the session from the registry.
                    if (drainProcesses)
                    {
                        try { loadedPath = workspace.GetStatus(workspaceId).LoadedPath; }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger?.LogDebug(
                                ex,
                                "workspace_close skipped process drain because loaded path lookup failed for workspace {WorkspaceId}.",
                                workspaceId);
                        }
                    }

                    var closed = workspace.Close(workspaceId);
                    _ = NotifyResourcesChangedAsync(server, "workspace_close", logger);
                    return JsonSerializer.Serialize(new { success = closed, workspaceId }, JsonDefaults.Indented);
                },
                outerCt,
                applyStalenessPolicy: false).ConfigureAwait(false);
            gate.RemoveGate(workspaceId);

            if (drainProcesses && !string.IsNullOrWhiteSpace(loadedPath))
            {
                var workingDirectory = Path.GetDirectoryName(loadedPath);
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    try
                    {
                        var drain = await commandRunner.RunAsync(
                            workingDirectory,
                            string.Empty,
                            ["build-server", "shutdown"],
                            outerCt).ConfigureAwait(false);
                        if (!drain.Succeeded)
                        {
                            logger?.LogDebug(
                                "workspace_close process drain exited with code {ExitCode} for workspace {WorkspaceId} in {WorkingDirectory}.",
                                drain.ExitCode,
                                workspaceId,
                                workingDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Non-zero exit or exception from drain is a warning, not an error.
                        // The close itself has already succeeded; callers receive the original payload.
                        logger?.LogDebug(
                            ex,
                            "workspace_close process drain failed for workspace {WorkspaceId} in {WorkingDirectory}.",
                            workspaceId,
                            workingDirectory);
                    }
                }
            }

            return json;
        }, ct);
    }

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List all currently loaded workspace sessions. Returns a lean summary per workspace by default — pass verbose=true for the full per-project tree of every workspace.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "List active workspace sessions.",
        outputSchemaTypeRef: typeof(WorkspaceListDto))]
    public static Task<string> ListWorkspaces(
        IWorkspaceManager workspace,
        [Description("When true, return the full per-project tree and workspace diagnostics for each workspace. Default false returns only counts and load state.")] bool verbose = false)
    {
        var workspaces = workspace.ListWorkspaces();
        if (verbose)
        {
            // verbose mode emits the full WorkspaceStatusDto per workspace; the published
            // outputSchema describes the default (verbose=false) shape only. Verbose callers
            // still get valid JSON on the text channel — the structuredContent shape just
            // won't match the advertised schema in that mode (documented opt-out).
            return Task.FromResult(JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented));
        }

        var summaries = workspaces.Select(WorkspaceStatusSummaryDto.From).ToList();
        var payload = new WorkspaceListDto(summaries.Count, summaries);
        return Task.FromResult(JsonSerializer.Serialize(payload, JsonDefaults.Indented));
    }

    [McpServerTool(Name = "workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Cheap health check after workspace_load — call this first before compile_check or heavy tools. " +
        "Default (verbose=false) returns summary JSON: isReady, isStale, workspaceErrorCount, restoreHint, solutionFileName, counts. " +
        "Pass verbose=true for the full per-project tree and workspace diagnostics.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Inspect status, diagnostics, and stale-state information for a workspace.",
        outputSchemaTypeRef: typeof(WorkspaceStatusSummaryDto))]
    public static Task<string> GetWorkspaceStatus(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, return the full per-project tree and workspace diagnostics. Default false returns only counts and load state.")] bool verbose = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var status = await workspace.GetStatusAsync(workspaceId, c).ConfigureAwait(false);
            return verbose
                ? JsonSerializer.Serialize(status, JsonDefaults.Indented)
                : JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "workspace_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Alias for workspace_status with verbose=false — same summary JSON (isReady, restoreHint, solutionFileName, error counts). Use for agent bootstrap right after workspace_load.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Lean workspace readiness summary (alias of workspace_status verbose=false).",
        outputSchemaTypeRef: typeof(WorkspaceStatusSummaryDto))]
    public static Task<string> GetWorkspaceHealth(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default) =>
        GetWorkspaceStatus(gate, workspace, workspaceId, verbose: false, ct);

    [McpServerTool(Name = "workspace_readiness_report", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "First-run/onboarding readiness report for a loaded workspace, distinct from the incident-focused workspace_support_bundle. " +
        "Uses existing read-side workspace probes only; it does not run tests or dotnet build by default. " +
        "Returns one compact verdict: ready, restore-needed, build-needed, or analyzer-limited, plus limitations and next recommended workflows. " +
        "If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "First-run workspace readiness report with verdict, limitations, and next workflows.",
        outputSchemaTypeRef: typeof(WorkspaceReadinessReportDto))]
    public static Task<string> GetWorkspaceReadinessReport(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Optional workspace session identifier returned by workspace_load. Omit only when zero or one workspace is loaded; pass explicitly when multiple workspaces are active.")] string? workspaceId = null,
        CancellationToken ct = default)
    {
        var loadedSummaries = workspace.ListWorkspaces()
            .Select(WorkspaceStatusSummaryDto.From)
            .ToArray();
        var resolvedWorkspaceId = ResolveOptionalWorkspaceId(workspaceId, loadedSummaries);
        if (resolvedWorkspaceId is null)
        {
            var status = loadedSummaries.Length == 0
                ? "no-workspace-loaded"
                : "workspace-id-required";
            var report = CreateReadinessReportWithoutTarget(status, workspaceId, loadedSummaries);
            return Task.FromResult(JsonSerializer.Serialize(report, JsonDefaults.Indented));
        }

        if (!workspace.ContainsWorkspace(resolvedWorkspaceId))
        {
            var report = CreateReadinessReportWithoutTarget(
                "workspace-not-found",
                resolvedWorkspaceId,
                loadedSummaries);
            return Task.FromResult(JsonSerializer.Serialize(report, JsonDefaults.Indented));
        }

        return gate.RunReadAsync(resolvedWorkspaceId, async c =>
        {
            var status = await workspace.GetStatusAsync(resolvedWorkspaceId, c).ConfigureAwait(false);
            var summary = WorkspaceStatusSummaryDto.From(status);
            int? sourceGeneratedDocumentCount = null;
            string? sourceGeneratedProbeLimitation = null;
            try
            {
                var generatedDocuments = await workspace.GetSourceGeneratedDocumentsAsync(
                    resolvedWorkspaceId,
                    projectName: null,
                    c).ConfigureAwait(false);
                sourceGeneratedDocumentCount = generatedDocuments.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sourceGeneratedProbeLimitation =
                    $"source_generated_documents probe was skipped after {ex.GetType().Name}; run source_generated_documents directly after resolving readiness blockers.";
            }

            var report = CreateReadinessReport(
                requestedWorkspaceId: workspaceId,
                loadedWorkspaces: loadedSummaries,
                status: status,
                summary: summary,
                sourceGeneratedDocumentCount: sourceGeneratedDocumentCount,
                sourceGeneratedProbeLimitation: sourceGeneratedProbeLimitation);

            return JsonSerializer.Serialize(report, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "workspace_support_bundle", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Incident-diagnosis bundle for an existing workspace session, distinct from first-run readiness reporting. " +
        "Composes loaded path, readiness summary, drift status, capped change ledger, workspace/surface version, diagnostics totals, and next-action hints. " +
        "Does not include source snippets. If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Incident support bundle: readiness, drift, changes, versions, diagnostics totals, and recovery hints without source snippets.",
        outputSchemaTypeRef: typeof(WorkspaceSupportBundleDto))]
    public static Task<string> GetWorkspaceSupportBundle(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IWorkspaceDriftService driftService,
        IChangeTracker changeTracker,
        IDiagnosticService diagnosticService,
        [Description("Optional workspace session identifier returned by workspace_load. Omit only when zero or one workspace is loaded; pass explicitly when multiple workspaces are active.")] string? workspaceId = null,
        [Description("Maximum number of recent change-ledger entries to include. Clamped to 0..50. Default 20.")] int maxChangeEntries = DefaultSupportBundleChangeCap,
        [Description("Maximum number of drifted file paths to include. Clamped to 0..100. Default 25.")] int maxDriftedFiles = DefaultSupportBundleDriftCap,
        CancellationToken ct = default)
    {
        var loadedSummaries = workspace.ListWorkspaces()
            .Select(WorkspaceStatusSummaryDto.From)
            .ToArray();
        var resolvedWorkspaceId = ResolveOptionalWorkspaceId(workspaceId, loadedSummaries);
        if (resolvedWorkspaceId is null)
        {
            var status = loadedSummaries.Length == 0
                ? "no-workspace-loaded"
                : "workspace-id-required";
            var bundle = CreateSupportBundleWithoutTarget(
                status,
                workspaceId,
                loadedSummaries,
                NormalizeCap(maxChangeEntries, MaxSupportBundleChangeCap),
                NormalizeCap(maxDriftedFiles, MaxSupportBundleDriftCap));
            return Task.FromResult(JsonSerializer.Serialize(bundle, JsonDefaults.Indented));
        }

        if (!workspace.ContainsWorkspace(resolvedWorkspaceId))
        {
            var bundle = CreateSupportBundleWithoutTarget(
                "workspace-not-found",
                resolvedWorkspaceId,
                loadedSummaries,
                NormalizeCap(maxChangeEntries, MaxSupportBundleChangeCap),
                NormalizeCap(maxDriftedFiles, MaxSupportBundleDriftCap));
            return Task.FromResult(JsonSerializer.Serialize(bundle, JsonDefaults.Indented));
        }

        var changeCap = NormalizeCap(maxChangeEntries, MaxSupportBundleChangeCap);
        var driftCap = NormalizeCap(maxDriftedFiles, MaxSupportBundleDriftCap);
        return gate.RunReadAsync(resolvedWorkspaceId, async c =>
        {
            var status = await workspace.GetStatusAsync(resolvedWorkspaceId, c).ConfigureAwait(false);
            var readiness = WorkspaceStatusSummaryDto.From(status);
            var drift = await driftService.CheckDriftAsync(resolvedWorkspaceId, c).ConfigureAwait(false);
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(
                resolvedWorkspaceId,
                projectFilter: null,
                fileFilter: null,
                severityFilter: "Warning",
                diagnosticIdFilter: null,
                c).ConfigureAwait(false);
            var changes = changeTracker.GetChanges(resolvedWorkspaceId);

            var bundle = CreateSupportBundle(
                requestedWorkspaceId: workspaceId,
                loadedWorkspaces: loadedSummaries,
                readiness: readiness,
                drift: drift,
                diagnostics: diagnostics,
                changes: changes,
                changeCap: changeCap,
                driftCap: driftCap);

            return JsonSerializer.Serialize(bundle, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "project_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get the project dependency graph and project metadata for a loaded workspace")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Inspect project and dependency structure.")]
    public static Task<string> GetProjectGraph(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, _ =>
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            return Task.FromResult(JsonSerializer.Serialize(graph, JsonDefaults.Indented));
        }, ct);
    }

    [McpServerTool(Name = "source_generated_documents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List source-generated documents for a workspace or specific project")]
    [McpToolMetadata("workspace", "stable", true, false,
        "List source-generated documents for a workspace or project.")]
    public static Task<string> GetSourceGeneratedDocuments(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => workspace.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, c),
            ct);

    [McpServerTool(Name = "get_source_text", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Read source text of a document in the loaded workspace. By default returns the full file. Pass startLine/endLine (1-based, inclusive) to slice. Output is capped at maxChars (default 65536); set Truncated=true marker indicates the response was clipped — re-request a narrower line range. Always returns RequestedStartLine/RequestedEndLine, ReturnedStartLine/ReturnedEndLine, TotalLineCount so callers can verify the slice.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Read source text as the Roslyn workspace currently sees it (may differ from disk if workspace hasn't been reloaded).")]
    public static Task<string> GetSourceText(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("Optional: 1-based first line to return (inclusive). Defaults to 1.")] int? startLine = null,
        [Description("Optional: 1-based last line to return (inclusive). Defaults to the last line of the file.")] int? endLine = null,
        [Description("Maximum characters to return (default 65536). Truncates with a marker if the requested range exceeds the cap.")] int maxChars = 65536,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            if (maxChars <= 0)
                throw new ArgumentException($"maxChars must be greater than 0 (got {maxChars}).", nameof(maxChars));
            if (startLine is < 1)
                throw new ArgumentException($"startLine must be >= 1 (got {startLine.Value}).", nameof(startLine));
            if (endLine is < 1)
                throw new ArgumentException($"endLine must be >= 1 (got {endLine.Value}).", nameof(endLine));
            if (startLine.HasValue && endLine.HasValue && startLine.Value > endLine.Value)
                throw new ArgumentException(
                    $"startLine ({startLine.Value}) must be <= endLine ({endLine.Value}).",
                    nameof(startLine));

            var text = await workspace.GetSourceTextAsync(workspaceId, filePath, c);
            if (text is null) throw new KeyNotFoundException($"Document not found: {filePath}");

            var totalLineCount = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.CountLines(text);
            var requestedStart = startLine ?? 1;
            var requestedEnd = endLine ?? totalLineCount;

            if (requestedStart > totalLineCount)
                throw new ArgumentException(
                    $"startLine ({requestedStart}) is past the end of the file ({totalLineCount} lines).",
                    nameof(startLine));

            // Clamp endLine to the file end so callers asking for "lines 100..1000" on a
            // 200-line file get lines 100..200 instead of an error.
            var returnedEnd = Math.Min(requestedEnd, totalLineCount);
            var returnedStart = requestedStart;

            var slice = RoslynMcp.Roslyn.Helpers.SourceTextSlicer.SliceLines(text, returnedStart, returnedEnd);

            var truncated = false;
            if (slice.Length > maxChars)
            {
                slice = slice.Substring(0, maxChars) + $"\n[TRUNCATED at {maxChars} characters — re-request a narrower line range to see the rest]";
                truncated = true;
            }

            return JsonSerializer.Serialize(new
            {
                filePath,
                totalLineCount,
                requestedStartLine = requestedStart,
                requestedEndLine = requestedEnd,
                returnedStartLine = returnedStart,
                returnedEndLine = returnedEnd,
                truncated,
                text = slice
            }, JsonDefaults.Indented);
        }, ct);
    }

    internal static async Task<WorkspaceStatusDto> RestoreAndReloadIfRequiredAsync(
        IDotnetCommandRunner commandRunner,
        IWorkspaceManager workspace,
        WorkspaceStatusDto status,
        bool autoRestore,
        CancellationToken ct)
    {
        if (!autoRestore || !status.RestoreRequired || string.IsNullOrWhiteSpace(status.LoadedPath))
        {
            return status;
        }

        var workingDirectory = Path.GetDirectoryName(status.LoadedPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException(
                $"workspace auto-restore could not determine a working directory for '{status.LoadedPath}'.");
        }

        var execution = await commandRunner.RunAsync(
            workingDirectory,
            status.LoadedPath,
            ["restore", status.LoadedPath, "--nologo"],
            ct).ConfigureAwait(false);

        if (!execution.Succeeded)
        {
            throw new InvalidOperationException(BuildRestoreFailureMessage(status.LoadedPath, execution));
        }

        return await workspace.ReloadAsync(status.WorkspaceId, ct).ConfigureAwait(false);
    }

    private static string SerializeWorkspaceLoadResult(
        WorkspaceStatusDto status,
        bool verbose,
        WorkspaceWarmResult? prewarmResult)
    {
        if (prewarmResult is null)
        {
            return verbose
                ? JsonSerializer.Serialize(status, JsonDefaults.Indented)
                : JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        }

        var payloadJson = verbose
            ? JsonSerializer.Serialize(status, JsonDefaults.Indented)
            : JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new InvalidOperationException("workspace_load response root must serialize as a JSON object.");

        payload["prewarm"] = JsonSerializer.SerializeToNode(prewarmResult, JsonDefaults.Indented);
        return payload.ToJsonString(JsonDefaults.Indented);
    }

    private static bool ShouldPrewarmAfterLoad(bool? prewarm, WorkspaceStatusDto status) =>
        prewarm ?? status.ProjectCount > AutoPrewarmProjectThreshold;

    private static string? ResolveOptionalWorkspaceId(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces)
    {
        if (!string.IsNullOrWhiteSpace(requestedWorkspaceId))
        {
            return requestedWorkspaceId;
        }

        return loadedWorkspaces.Count == 1 ? loadedWorkspaces[0].WorkspaceId : null;
    }

    private static WorkspaceReadinessReportDto CreateReadinessReport(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        WorkspaceStatusDto status,
        WorkspaceStatusSummaryDto summary,
        int? sourceGeneratedDocumentCount,
        string? sourceGeneratedProbeLimitation)
    {
        var verdict = ResolveReadinessVerdict(summary, status.WorkspaceDiagnostics);
        var limitations = BuildReadinessLimitations(summary, sourceGeneratedProbeLimitation);
        var target = new WorkspaceReadinessTargetDto(
            WorkspaceId: summary.WorkspaceId,
            LoadedPath: summary.LoadedPath,
            Verdict: verdict,
            ReadinessSummary: summary,
            ProjectCount: status.ProjectCount,
            DocumentCount: status.DocumentCount,
            TestProjectCount: status.Projects.Count(project => project.IsTestProject),
            SourceGeneratedDocumentCount: sourceGeneratedDocumentCount,
            Signals: BuildReadinessSignals(summary, status.WorkspaceDiagnostics));

        return new WorkspaceReadinessReportDto(
            ReportKind: "first-run-readiness",
            Status: "reported",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: target,
            Limitations: limitations,
            NextRecommendedWorkflows: BuildReadinessWorkflows(verdict, summary));
    }

    private static WorkspaceReadinessReportDto CreateReadinessReportWithoutTarget(
        string status,
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces)
    {
        WorkspaceRecommendedWorkflowDto[] workflows = status switch
        {
            "no-workspace-loaded" =>
            [
                new("workspace_load", "Load a .sln, .slnx, or .csproj path before requesting a readiness report.")
            ],
            "workspace-id-required" =>
            [
                new("workspace_readiness_report", "Pass workspaceId because multiple workspaces are loaded.")
            ],
            "workspace-not-found" =>
            [
                new("workspace_list", "Pick a current workspaceId, or call workspace_load again if the host restarted or the workspace was closed or evicted.")
            ],
            _ =>
            [
                new("workspace_readiness_report", "Retry with a valid workspaceId.")
            ]
        };

        return new WorkspaceReadinessReportDto(
            ReportKind: "first-run-readiness",
            Status: status,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: null,
            Limitations:
            [
                "No tests, dotnet build, or dotnet restore were run by this report."
            ],
            NextRecommendedWorkflows: workflows);
    }

    private static string ResolveReadinessVerdict(
        WorkspaceStatusSummaryDto summary,
        IReadOnlyList<DiagnosticDto> workspaceDiagnostics)
    {
        if (HasBuildRequiredDiagnostic(workspaceDiagnostics))
        {
            return ReadinessVerdictBuildNeeded;
        }

        if (summary.RestoreRequired)
        {
            return ReadinessVerdictRestoreNeeded;
        }

        if (!summary.AnalyzersReady)
        {
            return ReadinessVerdictAnalyzerLimited;
        }

        if (summary.IsReady)
        {
            return ReadinessVerdictReady;
        }

        return summary.WorkspaceErrorCount > 0 || summary.IsStale
            ? ReadinessVerdictBuildNeeded
            : ReadinessVerdictAnalyzerLimited;
    }

    private static IReadOnlyList<string> BuildReadinessLimitations(
        WorkspaceStatusSummaryDto summary,
        string? sourceGeneratedProbeLimitation)
    {
        var limitations = new List<string>
        {
            "No tests, dotnet build, or dotnet restore were run by this report.",
            "The report reflects the current MSBuildWorkspace snapshot; run workspace_reload after external file edits.",
            "source_generated_documents is capped by the host setting ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS."
        };

        if (!summary.AnalyzersReady)
        {
            limitations.Add("Analyzer-driven tools may under-report until analyzer warnings are resolved.");
        }

        if (!string.IsNullOrWhiteSpace(sourceGeneratedProbeLimitation))
        {
            limitations.Add(sourceGeneratedProbeLimitation);
        }

        return limitations.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> BuildReadinessSignals(
        WorkspaceStatusSummaryDto summary,
        IReadOnlyList<DiagnosticDto> workspaceDiagnostics)
    {
        var signals = new List<string>
        {
            $"projects={summary.ProjectCount}",
            $"documents={summary.DocumentCount}",
            $"workspaceDiagnostics={summary.WorkspaceDiagnosticCount}",
            $"workspaceErrors={summary.WorkspaceErrorCount}",
            $"workspaceWarnings={summary.WorkspaceWarningCount}",
            $"restoreRequired={summary.RestoreRequired.ToString().ToLowerInvariant()}",
            $"analyzersReady={summary.AnalyzersReady.ToString().ToLowerInvariant()}",
            $"isStale={summary.IsStale.ToString().ToLowerInvariant()}"
        };

        if (HasBuildRequiredDiagnostic(workspaceDiagnostics))
        {
            signals.Add("buildRequired=true");
        }

        if (!string.IsNullOrWhiteSpace(summary.RestoreHint))
        {
            signals.Add($"hint={summary.RestoreHint}");
        }

        return signals;
    }

    private static IReadOnlyList<WorkspaceRecommendedWorkflowDto> BuildReadinessWorkflows(
        string verdict,
        WorkspaceStatusSummaryDto summary)
    {
        return verdict switch
        {
            ReadinessVerdictReady =>
            [
                new("recommend_workflow", "Choose the next task-specific workflow now that the workspace is ready."),
                new("symbol_search", "Start semantic navigation against the loaded solution."),
                new("compile_check", "Run a read-side compile check when you need build diagnostics; this report did not run one."),
                new("test_discover", "Inspect test discoverability when onboarding needs test entry points; this report did not run tests.")
            ],
            ReadinessVerdictRestoreNeeded =>
            [
                new("dotnet restore + workspace_reload", "Restore package assets for the loaded solution or project, then refresh the workspace snapshot."),
                new("workspace_load(autoRestore=true)", "Use this on the next load if the client is allowed to run dotnet restore automatically.")
            ],
            ReadinessVerdictBuildNeeded =>
            [
                new("dotnet build + workspace_reload", "Build missing analyzer or project outputs, then refresh the workspace snapshot."),
                new("compile_check", "After reload, inspect remaining compiler diagnostics without running tests.")
            ],
            ReadinessVerdictAnalyzerLimited =>
            [
                new("workspace_status(verbose=true)", "Inspect workspace diagnostics and project metadata before trusting analyzer-driven tools."),
                new("project_diagnostics", "Use diagnostics with analyzer limitations in mind until analyzer readiness is resolved.")
            ],
            _ =>
            [
                new("workspace_status", "Re-check the workspace status before continuing.")
            ]
        };
    }

    private static bool HasBuildRequiredDiagnostic(IReadOnlyList<DiagnosticDto> workspaceDiagnostics)
    {
        foreach (var diagnostic in workspaceDiagnostics)
        {
            var message = diagnostic.Message ?? string.Empty;
            if (string.Equals(diagnostic.Id, "WORKSPACE_UNRESOLVED_ANALYZER", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("build output", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("missing analyzer build", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (message.Contains("Run `dotnet build`", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Run dotnet build", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static WorkspaceSupportBundleDto CreateSupportBundle(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        DiagnosticsResultDto diagnostics,
        IReadOnlyList<WorkspaceChangeDto> changes,
        int changeCap,
        int driftCap)
    {
        var returnedChanges = TakeRecentChanges(changes, changeCap);
        var returnedDriftedFiles = drift.FilesDrifted.Take(driftCap).ToArray();
        var target = new WorkspaceSupportTargetDto(
            WorkspaceId: readiness.WorkspaceId,
            LoadedPath: readiness.LoadedPath,
            ReadinessSummary: readiness,
            Drift: new WorkspaceSupportDriftDto(
                Stale: drift.Stale,
                TotalDriftedFiles: drift.FilesDrifted.Count,
                ReturnedDriftedFiles: returnedDriftedFiles.Length,
                HasMore: drift.FilesDrifted.Count > returnedDriftedFiles.Length,
                FilesDrifted: returnedDriftedFiles,
                Recommended: drift.Recommended),
            Version: new WorkspaceSupportVersionDto(
                WorkspaceVersion: readiness.WorkspaceVersion,
                SnapshotToken: readiness.SnapshotToken,
                ServerVersion: ResolveServerVersion(),
                CatalogVersion: ServerSurfaceCatalog.CatalogVersion));

        var diagnosticTotals = new WorkspaceSupportDiagnosticsTotalsDto(
            TotalErrors: diagnostics.TotalErrors,
            TotalWarnings: diagnostics.TotalWarnings,
            TotalInfo: diagnostics.TotalInfo,
            CompilerErrors: diagnostics.CompilerErrors,
            AnalyzerErrors: diagnostics.AnalyzerErrors,
            WorkspaceErrors: diagnostics.WorkspaceErrors);

        var ledger = new WorkspaceSupportChangeLedgerDto(
            TotalChanges: changes.Count,
            ReturnedChanges: returnedChanges.Count,
            HasMore: changes.Count > returnedChanges.Count,
            Changes: returnedChanges);

        var nextActions = BuildSupportBundleNextActions(
            readiness,
            drift,
            diagnosticTotals,
            ledger);

        return new WorkspaceSupportBundleDto(
            BundleKind: "incident-support",
            Status: ResolveSupportBundleStatus(readiness, drift, diagnosticTotals, ledger),
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: target,
            DiagnosticsTotals: diagnosticTotals,
            ChangeLedger: ledger,
            Limits: new WorkspaceSupportLimitsDto(changeCap, driftCap, SourceSnippetsIncluded: false),
            NextActions: nextActions);
    }

    private static WorkspaceSupportBundleDto CreateSupportBundleWithoutTarget(
        string status,
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces,
        int changeCap,
        int driftCap)
    {
        string[] nextActions = status switch
        {
            "no-workspace-loaded" =>
            [
                "Call workspace_load with a .sln, .slnx, or .csproj path before requesting a workspace support bundle."
            ],
            "workspace-id-required" =>
            [
                "Pass the workspaceId to workspace_support_bundle because multiple workspaces are loaded."
            ],
            "workspace-not-found" =>
            [
                "Call workspace_list to pick a current workspaceId, or call workspace_load again if the host restarted or the workspace was closed or evicted."
            ],
            _ => ["Retry workspace_support_bundle with a valid workspaceId."]
        };

        return new WorkspaceSupportBundleDto(
            BundleKind: "incident-support",
            Status: status,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedWorkspaceId: requestedWorkspaceId,
            LoadedWorkspaces: loadedWorkspaces,
            Workspace: null,
            DiagnosticsTotals: null,
            ChangeLedger: new WorkspaceSupportChangeLedgerDto(0, 0, HasMore: false, Changes: []),
            Limits: new WorkspaceSupportLimitsDto(changeCap, driftCap, SourceSnippetsIncluded: false),
            NextActions: nextActions);
    }

    private static IReadOnlyList<WorkspaceChangeDto> TakeRecentChanges(
        IReadOnlyList<WorkspaceChangeDto> changes,
        int cap)
    {
        if (cap == 0 || changes.Count == 0)
        {
            return [];
        }

        var skip = Math.Max(0, changes.Count - cap);
        return changes.Skip(skip).ToArray();
    }

    private static IReadOnlyList<string> BuildSupportBundleNextActions(
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        WorkspaceSupportDiagnosticsTotalsDto diagnostics,
        WorkspaceSupportChangeLedgerDto ledger)
    {
        var actions = new List<string>();
        if (drift.Stale || readiness.IsStale)
        {
            actions.Add($"Run workspace_reload for {readiness.WorkspaceId}; the workspace snapshot is stale or drifted from disk.");
        }

        if (readiness.RestoreRequired)
        {
            actions.Add("Run dotnet restore on the loaded solution or project, then workspace_reload.");
        }

        if (!readiness.AnalyzersReady)
        {
            actions.Add("Resolve analyzer-load warnings before trusting analyzer-driven tools such as project_diagnostics or find_unused_symbols.");
        }

        if (!string.IsNullOrWhiteSpace(readiness.RestoreHint))
        {
            actions.Add(readiness.RestoreHint);
        }

        if (diagnostics.TotalErrors > 0)
        {
            actions.Add("Run compile_check or project_diagnostics to inspect the error diagnostics.");
        }
        else if (diagnostics.TotalWarnings > 0)
        {
            actions.Add("Run project_diagnostics with filters if warning details are needed.");
        }
        else if (diagnostics.TotalInfo > 0)
        {
            actions.Add("Run project_diagnostics with severity=\"Info\" if informational diagnostic details are needed.");
        }

        if (ledger.TotalChanges > 0)
        {
            actions.Add("Run validate_recent_git_changes before handing off or continuing after session mutations.");
        }

        if (actions.Count == 0)
        {
            actions.Add("Workspace support bundle is clean; proceed with read-side tools or the intended workflow.");
        }

        return actions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ResolveSupportBundleStatus(
        WorkspaceStatusSummaryDto readiness,
        WorkspaceDriftResult drift,
        WorkspaceSupportDiagnosticsTotalsDto diagnostics,
        WorkspaceSupportChangeLedgerDto ledger)
    {
        if (drift.Stale || readiness.IsStale)
        {
            return "stale";
        }

        if (!readiness.IsReady || diagnostics.TotalErrors > 0 || diagnostics.TotalWarnings > 0 || diagnostics.TotalInfo > 0)
        {
            return "attention-needed";
        }

        return ledger.TotalChanges > 0 ? "changed" : "clean";
    }

    private static int NormalizeCap(int requested, int max) =>
        Math.Clamp(requested, 0, max);

    private static string ResolveServerVersion()
    {
        var assembly = typeof(WorkspaceTools).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private static ILogger? CreateLogger(ILoggerFactory? loggerFactory) =>
        loggerFactory?.CreateLogger(typeof(WorkspaceTools).FullName ?? nameof(WorkspaceTools));

    private static string BuildRestoreFailureMessage(string targetPath, CommandExecutionDto execution)
    {
        static string TrimOutput(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty)";
            }

            var trimmed = text.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed[^500..];
        }

        return
            $"workspace auto-restore failed for '{targetPath}' (exit code {execution.ExitCode}). " +
            $"stdout tail: {TrimOutput(execution.StdOut)} stderr tail: {TrimOutput(execution.StdErr)}";
    }

    /// <summary>
    /// Fire-and-forget notification to clients that the resource list has changed.
    /// </summary>
    private static async Task NotifyResourcesChangedAsync(McpServer server, string sourceTool, ILogger? logger)
    {
        try
        {
            await server.SendNotificationAsync(NotificationMethods.ResourceListChangedNotification).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Notification failure should not affect the tool result
            logger?.LogDebug(
                ex,
                "{SourceTool} could not notify clients that the resource list changed.",
                sourceTool);
        }
    }

    [McpServerTool(Name = "workspace_changes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpToolMetadata("workspace", "stable", true, false,
        "List all mutations applied to a workspace during this session, with descriptions, affected files, tool names, and timestamps.")]
    [Description("List all mutations applied to a workspace during this session. Returns an ordered list of changes with descriptions, affected files, tool names, and timestamps. Use to understand what has been modified since workspace_load.")]
    public static Task<string> GetWorkspaceChanges(
        IWorkspaceExecutionGate gate,
        IChangeTracker changeTracker,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, _ =>
        {
            var changes = changeTracker.GetChanges(workspaceId);
            return Task.FromResult(JsonSerializer.Serialize(new { count = changes.Count, changes }, JsonDefaults.Indented));
        }, ct);
    }
}
