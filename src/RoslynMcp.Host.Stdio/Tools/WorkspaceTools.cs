using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Runtime;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private const int _autoPrewarmProjectThreshold = 50;
    private const int _defaultSupportBundleChangeCap = 20;
    private const int _maxSupportBundleChangeCap = 50;
    private const int _defaultSupportBundleDriftCap = 25;
    private const int _maxSupportBundleDriftCap = 100;
    internal static readonly TimeSpan DefaultProcessDrainTimeout = TimeSpan.FromSeconds(10);

    /// <remarks>
    /// <para>autoRestore is tri-state. Omitted: when a project has never been restored (no project.assets.json), run dotnet restore plus one follow-up reload; a failed or timed-out restore does not fail the load, the result keeps restoreRequired=true and adds a path-free restoreFailureReason. true: run dotnet restore plus one follow-up reload for any restoreRequired=true (package-version drift included) and fail the call when the restore fails. false: never restore.</para>
    /// <para>While restoreRequired=true remains in the result (including after an autoRestore attempt that did not clear it), the response carries a structured nextCall: { tool: "workspace_reload", arguments: { workspaceId, autoRestore: true } }. The field is omitted otherwise.</para>
    /// <para>Set prewarm=true to run the workspace_warm compilation/semantic-model prewarm after a successful load or auto-restore reload; set prewarm=false to opt out. When prewarm is omitted, solutions with more than 50 projects are prewarmed automatically. The response includes a prewarm result block only when warming ran.</para>
    /// <para>DocumentCount note: the per-project DocumentCount often exceeds the Compile item count reported by evaluate_msbuild_items by about 3, because the SDK auto-generates implicit-usings, AssemblyInfo, and GlobalUsings files that Roslyn includes in the document set but MSBuild does not list as explicit Compile items.</para>
    /// <para>Sessions persist for the lifetime of the stdio host process - there is NO inactivity TTL. A workspace can become unreachable if (a) the host process restarts (Cursor/Claude Code may relaunch the MCP server transparently between conversations), (b) workspace_close is called, or (c) the configured concurrent-workspace cap (ROSLYNMCP_MAX_WORKSPACES) forced an eviction. When a previously valid workspaceId returns "Workspace was not found", call workspace_load again rather than treating it as an error.</para>
    /// <para>Pass evictPolicy=lru to silently evict the least-recently-used idle workspace when the cap is reached instead of receiving a hard error.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description("Load a .sln, .slnx, or .csproj into a Roslyn workspace session for semantic analysis. Idempotent by path: loading an already-loaded file returns the existing workspaceId. Returns a lean summary; pass verbose=true for the full project tree.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Load a .sln, .slnx, or .csproj into a named Roslyn workspace session.")]
    public static Task<string> LoadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IWorkspaceWarmService warmService,
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions validationOptions,
        [Description("Absolute path to a .sln, .slnx, or .csproj file")] string path,
        [Description("When true, return the full per-project tree and workspace diagnostics. Default false returns only counts and load state.")] bool verbose = false,
        [Description("Restore policy. Omitted: run `dotnet restore` and reload once only when a project has never been restored (missing project.assets.json); a failed or timed-out restore is non-fatal and reported as restoreRequired=true plus restoreFailureReason. true: also restore on package-version drift, and fail the call when the restore fails. false: never restore.")] bool? autoRestore = null,
        [Description("When true, run `workspace_warm` immediately after the load (and any auto-restore reload) succeeds, then include the warm result in the response. When omitted, large solutions with more than 50 projects are prewarmed automatically. Pass false to opt out and preserve the cold-load profile.")] bool? prewarm = null,
        [Description("Requests one-level sanctioned-root expansion for a sibling worktree. This takes effect only when the server operator also sets ROSLYNMCP_ALLOW_ROOT_EXPANSION=true; client input alone never widens the boundary. Higher ancestors and filesystem roots are never widened.")] bool expandSanctionedRoots = false,
        [Description("Controls cap-reached behaviour. 'Strict' (default) throws with activeWorkspaces and lruCandidate context for one-round-trip self-recovery. 'Lru' silently evicts the least-recently-used idle workspace to make room for the new load.")] EvictPolicy evictPolicy = EvictPolicy.Strict,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
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
            var restoreOutcome = await RestoreAndReloadIfRequiredAsync(commandExecutor, validationOptions, workspace, status, autoRestore, c).ConfigureAwait(false);
            status = restoreOutcome.Status;
            if (expandSanctionedRoots)
            {
                // preview-apply-token-write-path-toctou: record the load-time expansion grant so a
                // later *_apply redemption re-derives the SAME boundary that admitted this
                // workspace. Without it, every document of an expansion-loaded sibling worktree
                // sits outside the un-widened roots and every apply would be refused.
                RootExpansionGrantRegistry.Grant(status.WorkspaceId);
            }

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
            return SerializeWorkspaceLoadResult(status, verbose, prewarmResult, restoreOutcome.FailureReason);
        }, ct);
    }

    /// <remarks>
    /// <para>Pass verbose=false for a compact readiness/version/count projection; the default verbose=true preserves the full project tree.</para>
    /// <para>While restoreRequired=true remains in the result, the response carries a structured nextCall (workspace_reload with autoRestore=true); the field is omitted otherwise.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_reload", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Workspace-scoped calls auto-reload stale state by default. Use this for an explicit reload; autoRestore=true runs dotnet restore and reloads once when restoreRequired=true (and fails if the restore fails), omitted restores only never-restored projects non-fatally, false never restores.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Reload an existing workspace session from disk.")]
    public static Task<string> ReloadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions validationOptions,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Restore policy. Omitted: run `dotnet restore` and reload once only when a project has never been restored (missing project.assets.json); a failed or timed-out restore is non-fatal and reported as restoreRequired=true plus restoreFailureReason. true: also restore on package-version drift, and fail the call when the restore fails. false: never restore.")] bool? autoRestore = null,
        [Description("When true (default), preserve the full per-project response. Pass false for readiness, version, and aggregate counts without the project tree.")] bool verbose = true,
        CancellationToken ct = default)
    {
        // Reload acquires both the global load gate AND the per-workspace write lock so that
        // any in-flight readers on this workspace complete before the solution is replaced.
        return gate.RunLoadGateAsync(outerCt =>
            gate.RunWriteAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.ReloadAsync(workspaceId, innerCt).ConfigureAwait(false);
                var restoreOutcome = await RestoreAndReloadIfRequiredAsync(commandExecutor, validationOptions, workspace, status, autoRestore, innerCt).ConfigureAwait(false);
                return SerializeWorkspaceLoadResult(restoreOutcome.Status, verbose, prewarmResult: null, restoreOutcome.FailureReason);
            }, outerCt), ct);
    }

    /// <remarks>
    /// <para>drainProcesses=true runs `dotnet build-server shutdown` AND terminates any detached test-runner processes (testhost / vstest.console) whose executable lives under the loaded path directory, after session removal. On Windows this is what releases the MSBuild build-server and test-host file-system locks.</para>
    /// <para>When the drain leaves a candidate running, the response adds an `undrainedProcesses` array of { processName, processId, reason }. Reasons: `access-denied` or `path-query-failed` (the executable path could not be read, so the process was not killed), `terminate-failed`, or `drain-cancelled` (the cleanup budget or caller cancellation ended the drain first). The field is omitted when every candidate was handled.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources. Set drainProcesses=true to also release MSBuild build-server and test-host file locks - required before `git worktree remove` in sweep teardown sequences.")]
    [McpToolMetadata("workspace", "stable", false, true,
        "Close a loaded workspace session and release resources.")]
    public static Task<string> CloseWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, run `dotnet build-server shutdown` AND kill detached testhost/vstest.console processes rooted under the loaded path's directory after session removal, to release MSBuild build-server and test-host out-of-process file locks. Default false. Set true before `git worktree remove` in sweep teardown.")] bool drainProcesses = false,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default,
        IUnexpectedExceptionReporter? exceptionReporter = null) =>
        CloseWorkspaceCore(
            gate,
            workspace,
            commandRunner,
            workspaceId,
            drainProcesses,
            loggerFactory,
            exceptionReporter,
            Process.GetProcessesByName,
            DefaultProcessDrainTimeout,
            ct);

    /// <summary>
    /// Implementation of <c>workspace_close</c>. The MCP SDK advertises every non-DI parameter of an
    /// <c>[McpServerTool]</c> method in the tool's input schema, so the test seams live here, on a
    /// non-tool method, instead of on <see cref="CloseWorkspace"/>.
    /// </summary>
    /// <param name="getProcessesByName">Process enumerator; tests inject a fake one.</param>
    /// <param name="processDrainTimeout">Bound on post-close cleanup, independent of the request
    /// lifetime; tests pass a short one to prove a stuck drain cannot retain the load gate.</param>
    /// <param name="resolveExecutablePath">Executable-path resolver for drain candidates;
    /// <see langword="null"/> selects <see cref="ProcessExecutablePathResolver.Resolve"/>. Tests
    /// inject one to simulate a candidate whose path cannot be read.</param>
    internal static Task<string> CloseWorkspaceCore(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        string workspaceId,
        bool drainProcesses,
        ILoggerFactory? loggerFactory,
        IUnexpectedExceptionReporter? exceptionReporter,
        Func<string, Process[]> getProcessesByName,
        TimeSpan processDrainTimeout,
        CancellationToken ct,
        Func<Process, ProcessExecutablePathResolution>? resolveExecutablePath = null)
    {
        ArgumentNullException.ThrowIfNull(getProcessesByName);
        resolveExecutablePath ??= ProcessExecutablePathResolver.Resolve;
        if (processDrainTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Process drain timeout must be positive (was {processDrainTimeout}).");
        }

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
            var closed = await gate.RunWriteAsync(
                workspaceId,
                innerCt =>
                {
                    // Capture the loaded path before close removes the session from the registry.
                    if (drainProcesses)
                    {
                        try { loadedPath = workspace.GetStatus(workspaceId).LoadedPath; }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // An unknown/already-closed workspace is reflected by success=false below.
                            // Do not emit raw exception detail for this expected lookup miss.
                        }
                    }

                    return Task.FromResult(workspace.Close(workspaceId));
                },
                outerCt,
                applyStalenessPolicy: false).ConfigureAwait(false);
            gate.RemoveGate(workspaceId);

            IReadOnlyList<UndrainedProcess>? undrainedProcesses = null;
            if (drainProcesses && !string.IsNullOrWhiteSpace(loadedPath))
            {
                var workingDirectory = Path.GetDirectoryName(loadedPath);
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
                    cleanupCts.CancelAfter(processDrainTimeout);
                    Exception? cleanupFailure = null;
                    try
                    {
                        var drain = await commandRunner.RunAsync(
                            workingDirectory,
                            string.Empty,
                            ["build-server", "shutdown"],
                            cleanupCts.Token).ConfigureAwait(false);
                        if (!drain.Succeeded)
                        {
                            cleanupFailure = new InvalidOperationException(
                                $"Process drain exited with code {drain.ExitCode}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Workspace removal is the commit point. Cleanup observes caller cancellation
                        // and its own timeout, but neither can roll the committed close result back.
                        cleanupFailure = ex;
                    }

                    // Second drain sub-step: `dotnet test` spawns detached testhost.exe /
                    // vstest.console.exe child processes that survive build-server shutdown and
                    // keep tests/.../bin file handles open, blocking `git worktree remove` on
                    // Windows. Terminate any whose executable lives under this working directory.
                    // This step runs even after cancellation: it then terminates nothing, but it
                    // still reports each in-workspace candidate it left running.
                    var testHostDrain = DetachedTestHostDrain.Run(
                        workingDirectory,
                        getProcessesByName,
                        resolveExecutablePath,
                        logger,
                        workspaceId,
                        cleanupCts.Token);
                    cleanupFailure ??= testHostDrain.FirstFailure;
                    if (testHostDrain.Undrained.Count > 0)
                    {
                        undrainedProcesses = testHostDrain.Undrained;
                    }

                    if (cleanupFailure is not null)
                    {
                        ReportProcessDrainFailure(exceptionReporter, cleanupFailure);
                    }
                }
            }

            return JsonSerializer.Serialize(
                new WorkspaceCloseResult(closed, workspaceId, undrainedProcesses),
                JsonDefaults.Indented);
        }, ct);
    }

    /// <summary>
    /// <c>workspace_close</c> response. <see cref="UndrainedProcesses"/> is additive and is
    /// omitted unless the drain left a candidate running.
    /// </summary>
    private sealed record WorkspaceCloseResult(
        bool Success,
        string WorkspaceId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<UndrainedProcess>? UndrainedProcesses);

    private static void ReportProcessDrainFailure(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception) =>
        UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.WorkspaceCloseProcessDrain);

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceListDto)), Description("List all currently loaded workspace sessions. Returns a lean summary per workspace by default — pass verbose=true for the full per-project tree of every workspace.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "List active workspace sessions.")]
    public static Task<CallToolResult> ListWorkspaces(
        IWorkspaceManager workspace,
        [Description("When true, return the full per-project tree and workspace diagnostics for each workspace. Default false returns only counts and load state.")] bool verbose = false)
    {
        var workspaces = workspace.ListWorkspaces();
        if (verbose)
        {
            var verbosePayload = new WorkspaceListVerboseDto(workspaces.Count, workspaces);
            return Task.FromResult(StructuredToolResult.Create(verbosePayload));
        }

        var summaries = workspaces.Select(WorkspaceStatusSummaryDto.From).ToList();
        var payload = new WorkspaceListDto(summaries.Count, summaries);
        return Task.FromResult(StructuredToolResult.Create(payload));
    }

    /// <remarks>
    /// <para>Pass verbose=true for the full per-project tree and workspace diagnostics.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceStatusSummaryDto)), Description("Cheap health check after workspace_load - call this first before compile_check or heavy tools. Default (verbose=false) returns summary JSON: isReady, isStale, workspaceErrorCount, restoreHint, solutionFileName, counts.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Inspect status, diagnostics, and stale-state information for a workspace.")]
    public static Task<CallToolResult> GetWorkspaceStatus(
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
                ? StructuredToolResult.Create(status)
                : StructuredToolResult.Create(WorkspaceStatusSummaryDto.From(status));
        }, ct);
    }

    [McpServerTool(Name = "workspace_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceStatusSummaryDto)), Description(
        "Alias for workspace_status with verbose=false — same summary JSON (isReady, restoreHint, solutionFileName, error counts). Use for agent bootstrap right after workspace_load.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Lean workspace readiness summary (alias of workspace_status verbose=false).")]
    public static Task<CallToolResult> GetWorkspaceHealth(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default) =>
        GetWorkspaceStatus(gate, workspace, workspaceId, verbose: false, ct);

    /// <remarks>
    /// <para>Uses existing read-side workspace probes only; it does not run tests or dotnet build by default.</para>
    /// <para>If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_readiness_report", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceReadinessReportDto)), Description("First-run/onboarding readiness report for a loaded workspace, distinct from the incident-focused workspace_support_bundle. Returns one verdict - ready, restore-needed, build-needed, or analyzer-limited - plus limitations and next workflows.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "First-run workspace readiness report with verdict, limitations, and next workflows.")]
    public static Task<CallToolResult> GetWorkspaceReadinessReport(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Optional workspace session identifier returned by workspace_load. Omit only when zero or one workspace is loaded; pass explicitly when multiple workspaces are active.")] string? workspaceId = null,
        CancellationToken ct = default,
        IUnexpectedExceptionReporter? exceptionReporter = null)
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
            var report = WorkspaceReadinessReportBuilder.CreateWithoutTarget(status, workspaceId, loadedSummaries);
            return Task.FromResult(StructuredToolResult.Create(report));
        }

        if (!workspace.ContainsWorkspace(resolvedWorkspaceId))
        {
            var report = WorkspaceReadinessReportBuilder.CreateWithoutTarget(
                "workspace-not-found",
                resolvedWorkspaceId,
                loadedSummaries);
            return Task.FromResult(StructuredToolResult.Create(report));
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
                var detail = UnexpectedExceptionReporting.Report(
                    exceptionReporter,
                    ex,
                    UnexpectedExceptionCategory.WorkspaceReadiness).Public;
                sourceGeneratedProbeLimitation =
                    $"source_generated_documents probe was skipped after {detail.Category}. " +
                    "Run source_generated_documents directly after resolving readiness blockers. " +
                    $"correlationId={detail.CorrelationId}";
            }

            var report = WorkspaceReadinessReportBuilder.Create(
                requestedWorkspaceId: workspaceId,
                loadedWorkspaces: loadedSummaries,
                status: status,
                summary: summary,
                sourceGeneratedDocumentCount: sourceGeneratedDocumentCount,
                sourceGeneratedProbeLimitation: sourceGeneratedProbeLimitation);

            return StructuredToolResult.Create(report);
        }, ct);
    }

    /// <remarks>
    /// <para>Also reports the loaded path and workspace/surface version. Does not include source snippets.</para>
    /// <para>If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_support_bundle", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceSupportBundleDto)), Description("Incident-diagnosis bundle for an existing workspace session, distinct from the first-run workspace_readiness_report. Composes readiness, drift status, a capped change ledger, versions, diagnostics totals, and next-action hints.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Incident support bundle: readiness, drift, changes, versions, diagnostics totals, and recovery hints without source snippets.")]
    public static Task<CallToolResult> GetWorkspaceSupportBundle(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IWorkspaceDriftService driftService,
        IChangeTracker changeTracker,
        IDiagnosticService diagnosticService,
        [Description("Optional workspace session identifier returned by workspace_load. Omit only when zero or one workspace is loaded; pass explicitly when multiple workspaces are active.")] string? workspaceId = null,
        [Description("Maximum number of recent change-ledger entries to include. Clamped to 0..50. Default 20.")] int maxChangeEntries = _defaultSupportBundleChangeCap,
        [Description("Maximum number of drifted file paths to include. Clamped to 0..100. Default 25.")] int maxDriftedFiles = _defaultSupportBundleDriftCap,
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
            var bundle = WorkspaceSupportBundleBuilder.CreateWithoutTarget(
                status,
                workspaceId,
                loadedSummaries,
                NormalizeCap(maxChangeEntries, _maxSupportBundleChangeCap),
                NormalizeCap(maxDriftedFiles, _maxSupportBundleDriftCap));
            return Task.FromResult(StructuredToolResult.Create(bundle));
        }

        if (!workspace.ContainsWorkspace(resolvedWorkspaceId))
        {
            var bundle = WorkspaceSupportBundleBuilder.CreateWithoutTarget(
                "workspace-not-found",
                resolvedWorkspaceId,
                loadedSummaries,
                NormalizeCap(maxChangeEntries, _maxSupportBundleChangeCap),
                NormalizeCap(maxDriftedFiles, _maxSupportBundleDriftCap));
            return Task.FromResult(StructuredToolResult.Create(bundle));
        }

        var changeCap = NormalizeCap(maxChangeEntries, _maxSupportBundleChangeCap);
        var driftCap = NormalizeCap(maxDriftedFiles, _maxSupportBundleDriftCap);
        return gate.RunReadAsync(resolvedWorkspaceId, async c =>
        {
            var status = await workspace.GetStatusAsync(resolvedWorkspaceId, c).ConfigureAwait(false);
            var readiness = WorkspaceStatusSummaryDto.From(status);
            var drift = await driftService.CheckDriftAsync(resolvedWorkspaceId, c).ConfigureAwait(false);
            diagnosticService.TryGetCachedWorkspaceDiagnostics(
                resolvedWorkspaceId,
                out var diagnostics);
            var changes = changeTracker.GetChanges(resolvedWorkspaceId);

            var bundle = WorkspaceSupportBundleBuilder.Create(
                requestedWorkspaceId: workspaceId,
                loadedWorkspaces: loadedSummaries,
                readiness: readiness,
                drift: drift,
                diagnostics: diagnostics,
                changes: changes,
                changeCap: changeCap,
                driftCap: driftCap);

            return StructuredToolResult.Create(bundle);
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

    /// <remarks>
    /// <para>maxChars defaults to 65536; a Truncated=true marker indicates the response was clipped - re-request a narrower line range.</para>
    /// <para>The response always returns RequestedStartLine/RequestedEndLine, ReturnedStartLine/ReturnedEndLine, and TotalLineCount so callers can verify the slice.</para>
    /// </remarks>
    [McpServerTool(Name = "get_source_text", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Read source text of a document as the loaded Roslyn workspace currently sees it (may differ from disk until reload). Returns the full file by default; pass startLine/endLine (1-based, inclusive) to slice. Output is capped at maxChars.")]
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
            SourceTextRequestProjection.ValidateRequest(maxChars, startLine, endLine);

            var text = await workspace.GetSourceTextAsync(workspaceId, filePath, c);
            if (text is null) throw new KeyNotFoundException($"Document not found: {filePath}");

            var projection = SourceTextRequestProjection.Project(filePath, text, startLine, endLine, maxChars);
            return JsonSerializer.Serialize(projection, JsonDefaults.Indented);
        }, ct);
    }

    /// <summary>Result of the load-time restore step: the (possibly reloaded) status plus, when a
    /// default (omitted-<c>autoRestore</c>) restore failed non-fatally, a path-free reason.</summary>
    internal readonly record struct RestoreReloadOutcome(WorkspaceStatusDto Status, string? FailureReason = null);

    /// <summary>
    /// Load-time restore step. <paramref name="autoRestore"/> is tri-state:
    /// <see langword="false"/> never restores; <see langword="true"/> restores for any
    /// <c>restoreRequired</c> (drift included) and throws on failure; <see langword="null"/>
    /// (omitted) restores only when a project has never been restored and reports a failed or
    /// timed-out restore as <see cref="RestoreReloadOutcome.FailureReason"/> instead of throwing.
    /// Caller cancellation always propagates.
    /// </summary>
    internal static async Task<RestoreReloadOutcome> RestoreAndReloadIfRequiredAsync(
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions validationOptions,
        IWorkspaceManager workspace,
        WorkspaceStatusDto status,
        bool? autoRestore,
        CancellationToken ct)
    {
        if (autoRestore == false || !status.RestoreRequired || string.IsNullOrWhiteSpace(status.LoadedPath))
        {
            return new RestoreReloadOutcome(status);
        }

        if (autoRestore is null)
        {
            if (!EnumerateProjectPaths(status).Any(RestoreStalenessDetector.HasMissingAssets))
            {
                return new RestoreReloadOutcome(status);
            }

            try
            {
                await RunRestoreAsync(commandExecutor, validationOptions, status, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or PublicInvalidOperationException && !ct.IsCancellationRequested)
            {
                // Default restore is best-effort: the workspace is already loaded, so report the
                // reason and leave restoreRequired=true. TimeoutException text embeds the dotnet
                // command line (absolute paths), so it is replaced by a fixed path-free reason.
                var reason = ex is PublicInvalidOperationException publicFailure
                    ? publicFailure.PublicMessage
                    : "workspace auto-restore timed out before completing; raise ROSLYNMCP_RESTORE_TIMEOUT_SECONDS or run dotnet restore for the loaded project and retry workspace_reload with autoRestore=true.";
                return new RestoreReloadOutcome(status, reason);
            }

            return new RestoreReloadOutcome(await workspace.ReloadAsync(status.WorkspaceId, ct).ConfigureAwait(false));
        }

        await RunRestoreAsync(commandExecutor, validationOptions, status, ct).ConfigureAwait(false);
        return new RestoreReloadOutcome(await workspace.ReloadAsync(status.WorkspaceId, ct).ConfigureAwait(false));
    }

    private static async Task RunRestoreAsync(
        IGatedCommandExecutor commandExecutor,
        ValidationServiceOptions validationOptions,
        WorkspaceStatusDto status,
        CancellationToken ct)
    {
        // Route through the shared per-workspace command gate so the restore cannot write obj/
        // concurrently with build_workspace/test_run. Each invocation's timeout is a total budget that
        // includes the queue wait (GatedCommandExecutor.ExecuteAsync), so a held gate yields
        // TimeoutException. The restore phase has its own RestoreTimeout, clamped to what is left of
        // the enclosing request deadline minus a reserve for the reload below, so a slow restore
        // cannot consume the whole gate deadline and starve that reload.
        var phaseClock = Stopwatch.StartNew();
        foreach (var (targetPath, packagesPath) in PlanRestoreInvocations(status))
        {
            var execution = await commandExecutor.ExecuteAsync(
                status.WorkspaceId,
                targetPath,
                BuildRestoreArguments(targetPath, packagesPath),
                RemainingRestoreBudget(validationOptions, phaseClock.Elapsed),
                ct).ConfigureAwait(false);

            if (!execution.Succeeded)
            {
                // The detailed message (absolute path + output tails) rides as the inner exception for logs;
                // the public message is path-free so ToolErrorHandler can return it verbatim.
                throw new PublicInvalidOperationException(
                    $"workspace auto-restore failed (exit code {execution.ExitCode}); run dotnet restore for the loaded project and retry workspace_reload.",
                    new InvalidOperationException(BuildRestoreFailureMessage(targetPath, execution)));
            }
        }
    }

    /// <summary>
    /// Time left for the next restore invocation: <c>RestoreTimeout</c> minus the phase time already
    /// spent, clamped to the enclosing request deadline minus <c>RestoreReloadReserve</c> when a
    /// gate published one. Throws a path-free <see cref="TimeoutException"/> when nothing remains,
    /// before any <c>dotnet</c> process is spawned.
    /// </summary>
    private static TimeSpan RemainingRestoreBudget(ValidationServiceOptions options, TimeSpan phaseElapsed)
    {
        var budget = options.RestoreTimeout - phaseElapsed;
        if (RequestDeadline.Remaining is { } requestRemaining)
        {
            var beforeReserve = requestRemaining - options.RestoreReloadReserve;
            if (beforeReserve < budget)
            {
                budget = beforeReserve;
            }
        }

        if (budget <= TimeSpan.Zero)
        {
            throw new TimeoutException(
                "workspace auto-restore has no time budget left; raise ROSLYNMCP_RESTORE_TIMEOUT_SECONDS or ROSLYNMCP_REQUEST_TIMEOUT_SECONDS, " +
                "or run dotnet restore for the loaded project and retry workspace_reload.");
        }

        return budget;
    }

    /// <summary>
    /// workspace-restore-packages-path: decides which restore commands to run. Restore must write to
    /// the package folder the projects' existing <c>project.assets.json</c> recorded (e.g. a worktree
    /// scratch folder), not the host's default. One distinct recorded path (or none) → one restore of
    /// the loaded path; several distinct paths → one restore per project with its own path.
    /// </summary>
    private static IReadOnlyList<(string TargetPath, string? PackagesPath)> PlanRestoreInvocations(WorkspaceStatusDto status)
    {
        var loadedPath = status.LoadedPath!;
        var recorded = EnumerateProjectPaths(status)
            .Select(path => (Path: path, PackagesPath: RestoreStalenessDetector.TryReadRestorePackagesPath(path)))
            .ToList();
        var distinctPackagesPaths = recorded
            .Select(r => r.PackagesPath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinctPackagesPaths.Count > 1
            ? recorded.Select(r => (r.Path, r.PackagesPath)).ToList()
            : [(loadedPath, distinctPackagesPaths.SingleOrDefault())];
    }

    /// <summary>Project files of the loaded workspace; a single-<c>.csproj</c> load with no reported
    /// projects falls back to the loaded path itself.</summary>
    private static List<string> EnumerateProjectPaths(WorkspaceStatusDto status)
    {
        var projectPaths = status.Projects.Select(p => p.FilePath).ToList();
        if (projectPaths.Count == 0 && status.LoadedPath!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectPaths.Add(status.LoadedPath);
        }

        return projectPaths;
    }

    private static IReadOnlyList<string> BuildRestoreArguments(string targetPath, string? packagesPath) =>
        packagesPath is null
            ? ["restore", targetPath, "--nologo"]
            : ["restore", targetPath, "--nologo", "--packages", packagesPath];

    /// <summary>
    /// Serializes a <c>workspace_load</c>/<c>workspace_reload</c> result. Adds an additive
    /// <c>nextCall</c> (<c>workspace_reload</c> with <c>autoRestore=true</c>) whenever
    /// <see cref="WorkspaceStatusDto.RestoreRequired"/> is set, a path-free <c>restoreFailureReason</c> when
    /// a default (omitted-<c>autoRestore</c>) restore failed non-fatally, and a <c>prewarm</c> block when warming ran.
    /// <c>internal</c> so wire tests can assert the emitted JSON.
    /// </summary>
    internal static string SerializeWorkspaceLoadResult(
        WorkspaceStatusDto status,
        bool verbose,
        WorkspaceWarmResult? prewarmResult,
        string? restoreFailureReason = null)
    {
        var payloadJson = verbose
            ? JsonSerializer.Serialize(status, JsonDefaults.Indented)
            : JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        var payload = JsonNode.Parse(payloadJson) as JsonObject
            ?? throw new InvalidOperationException("workspace_load response root must serialize as a JSON object.");

        if (status.RestoreRequired)
        {
            payload["nextCall"] = JsonSerializer.SerializeToNode(
                NextCallDto.WorkspaceReloadWithAutoRestore(status.WorkspaceId), JsonDefaults.Indented);
        }

        if (!string.IsNullOrWhiteSpace(restoreFailureReason))
        {
            payload["restoreFailureReason"] = restoreFailureReason;
        }

        if (prewarmResult is not null)
        {
            payload["prewarm"] = JsonSerializer.SerializeToNode(prewarmResult, JsonDefaults.Indented);
        }

        return payload.ToJsonString(JsonDefaults.Indented);
    }

    private static bool ShouldPrewarmAfterLoad(bool? prewarm, WorkspaceStatusDto status) =>
        prewarm ?? status.ProjectCount > _autoPrewarmProjectThreshold;

    /// <summary>
    /// workspace-id-omitted-single-resolve: canonical single-workspace resolution shared by the
    /// workspace tools and the read-path middleware (<c>StructuredCallToolFilter</c>). Returns
    /// the requested id when supplied; otherwise the sole loaded workspace's id when exactly one
    /// is loaded; otherwise <see langword="null"/> (zero or ≥2 loaded — the caller decides
    /// between on-demand discovery and a structured fast-fail). <c>internal</c> so the middleware
    /// applies the same logic at the chokepoint rather than re-deriving it.
    /// </summary>
    internal static string? ResolveOptionalWorkspaceId(
        string? requestedWorkspaceId,
        IReadOnlyList<WorkspaceStatusSummaryDto> loadedWorkspaces)
    {
        if (!string.IsNullOrWhiteSpace(requestedWorkspaceId))
        {
            return requestedWorkspaceId;
        }

        return loadedWorkspaces.Count == 1 ? loadedWorkspaces[0].WorkspaceId : null;
    }

    private static int NormalizeCap(int requested, int max) =>
        Math.Clamp(requested, 0, max);

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
