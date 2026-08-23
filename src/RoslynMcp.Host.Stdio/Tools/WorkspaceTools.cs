using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Contracts;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private const int _autoPrewarmProjectThreshold = 50;
    private const int _defaultSupportBundleChangeCap = 20;
    private const int _maxSupportBundleChangeCap = 50;
    private const int _defaultSupportBundleDriftCap = 25;
    private const int _maxSupportBundleDriftCap = 100;
    private static readonly TimeSpan s_processDrainTimeout = TimeSpan.FromSeconds(10);

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
            status = await RestoreAndReloadIfRequiredAsync(commandRunner, workspace, status, autoRestore, c).ConfigureAwait(false);
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
            return SerializeWorkspaceLoadResult(status, verbose, prewarmResult);
        }, ct);
    }

    [McpServerTool(Name = "workspace_reload", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Reload the currently loaded workspace to pick up file changes. Set autoRestore=true to run dotnet restore and one follow-up reload when the reloaded status reports restoreRequired=true.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Reload an existing workspace session from disk.")]
    public static Task<string> ReloadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        IDotnetCommandRunner commandRunner,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true and the reloaded status reports restoreRequired=true, run `dotnet restore` on the loaded target and reload once before returning.")] bool autoRestore = false,
        CancellationToken ct = default)
    {
        // Reload acquires both the global load gate AND the per-workspace write lock so that
        // any in-flight readers on this workspace complete before the solution is replaced.
        return gate.RunLoadGateAsync(outerCt =>
            gate.RunWriteAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.ReloadAsync(workspaceId, innerCt).ConfigureAwait(false);
                status = await RestoreAndReloadIfRequiredAsync(commandRunner, workspace, status, autoRestore, innerCt).ConfigureAwait(false);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, outerCt), ct);
    }

    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources. Set drainProcesses=true to run `dotnet build-server shutdown` AND terminate any detached test-runner processes (testhost / vstest.console) whose executable lives under the loaded path's directory, after session removal — this releases MSBuild build-server and test-host file-system locks on Windows, which is required before `git worktree remove` in sweep teardown sequences.")]
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
        IUnexpectedExceptionReporter? exceptionReporter = null,
        // Seam: lets tests inject a fake process enumerator. Production default enumerates live
        // processes by name. Not surfaced to MCP callers — no [Description], so the schema omits it.
        Func<string, Process[]>? getProcessesByName = null,
        // Seam: production cleanup is bounded independently of the request lifetime. Tests use a
        // short timeout to prove that a stuck drain cannot retain the load gate indefinitely.
        TimeSpan? processDrainTimeout = null)
    {
        var logger = CreateLogger(loggerFactory);
        getProcessesByName ??= Process.GetProcessesByName;
        var cleanupTimeout = processDrainTimeout ?? s_processDrainTimeout;
        if (cleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processDrainTimeout),
                processDrainTimeout,
                "Process drain timeout must be positive.");
        }

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
                            // An unknown/already-closed workspace is reflected by success=false below.
                            // Do not emit raw exception detail for this expected lookup miss.
                        }
                    }

                    var closed = workspace.Close(workspaceId);
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
                    using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
                    cleanupCts.CancelAfter(cleanupTimeout);
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
                    if (!cleanupCts.IsCancellationRequested)
                    {
                        cleanupFailure ??= TryKillDetachedTestHosts(
                            workingDirectory,
                            getProcessesByName,
                            logger,
                            workspaceId,
                            cleanupCts.Token);
                    }

                    if (cleanupFailure is not null)
                    {
                        ReportProcessDrainFailure(exceptionReporter, cleanupFailure);
                    }
                }
            }

            return json;
        }, ct);
    }

    private static void ReportProcessDrainFailure(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception) =>
        UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.WorkspaceCloseProcessDrain);

    /// <summary>
    /// Best-effort termination of detached <c>testhost</c> / <c>vstest.console</c> processes whose
    /// executable resides under <paramref name="workingDirectory"/>. These are spawned by
    /// <c>dotnet test</c> and survive <c>dotnet build-server shutdown</c>, holding
    /// <c>tests/.../bin</c> file locks that block <c>git worktree remove</c> on Windows.
    /// </summary>
    /// <remarks>
    /// Entirely non-fatal: the workspace close has already succeeded by the time this runs, so any
    /// enumeration / inspection / kill failure is swallowed at Debug. <see cref="Process.MainModule"/>
    /// throws <see cref="System.ComponentModel.Win32Exception"/> for protected or already-exited
    /// processes — those are SKIPPED (never kill-all on an inaccessible path). The
    /// <paramref name="getProcessesByName"/> seam lets tests inject a fake enumerator.
    /// </remarks>
    private static Exception? TryKillDetachedTestHosts(
        string workingDirectory,
        Func<string, Process[]> getProcessesByName,
        ILogger? logger,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        Exception? firstFailure = null;
        // Boundary guard: only kill processes that are TRUE DESCENDANTS of workingDirectory.
        // A bare StartsWith(workingDirectory) false-positives on a sibling worktree whose path
        // is a string-prefix (e.g. workingDirectory ".../wt-foo" vs sibling ".../wt-foo-bar"),
        // which would Kill an unrelated worktree's testhost tree mid-test-run. Normalize to a
        // full path and append exactly one separator so the prefix can only match a child path.
        // .worktrees/ holds multiple concurrent sibling worktrees during parallel sweeps.
        string workingDirectoryPrefix;
        try
        {
            workingDirectoryPrefix =
                Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }
        catch (Exception ex)
        {
            // A malformed working directory cannot be normalized — skip the drain entirely
            // rather than fall back to an unguarded match.
            return ex;
        }

        // Match both the .NET-Core test host ("testhost") and the legacy console runner
        // ("vstest.console"). Names are extensionless on Process; .exe on Windows, bare on Unix.
        foreach (var processName in new[] { "testhost", "vstest.console" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process[] candidates;
            try
            {
                candidates = getProcessesByName(processName);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                continue;
            }

            foreach (var process in candidates)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // MainModule access throws Win32Exception for protected/exited processes —
                    // skip those rather than risk terminating an unrelated process.
                    var executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(executablePath))
                    {
                        continue;
                    }

                    // Compare full paths against the separator-terminated working-directory
                    // prefix so only true descendants match — never a bare string prefix that
                    // would catch a sibling worktree (".../wt-foo" vs ".../wt-foo-bar").
                    var fullExecutablePath = Path.GetFullPath(executablePath);
                    if (!fullExecutablePath.StartsWith(workingDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    logger?.LogDebug(
                        "workspace_close terminated detached {ProcessName} (pid {ProcessId}) for workspace {WorkspaceId}.",
                        processName,
                        process.Id,
                        workspaceId);
                }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch (Exception ex)
                    {
                        firstFailure ??= ex;
                    }
                }
            }
        }

        return firstFailure;
    }

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

    [McpServerTool(Name = "workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceStatusSummaryDto)), Description(
        "Cheap health check after workspace_load — call this first before compile_check or heavy tools. " +
        "Default (verbose=false) returns summary JSON: isReady, isStale, workspaceErrorCount, restoreHint, solutionFileName, counts. " +
        "Pass verbose=true for the full per-project tree and workspace diagnostics.")]
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

    [McpServerTool(Name = "workspace_readiness_report", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceReadinessReportDto)), Description(
        "First-run/onboarding readiness report for a loaded workspace, distinct from the incident-focused workspace_support_bundle. " +
        "Uses existing read-side workspace probes only; it does not run tests or dotnet build by default. " +
        "Returns one compact verdict: ready, restore-needed, build-needed, or analyzer-limited, plus limitations and next recommended workflows. " +
        "If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.")]
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

    [McpServerTool(Name = "workspace_support_bundle", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WorkspaceSupportBundleDto)), Description(
        "Incident-diagnosis bundle for an existing workspace session, distinct from first-run readiness reporting. " +
        "Composes loaded path, readiness summary, drift status, capped change ledger, workspace/surface version, diagnostics totals, and next-action hints. " +
        "Does not include source snippets. If workspaceId is omitted and exactly one workspace is loaded, that workspace is used; if none or multiple are loaded, the response explains the next action.")]
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
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(
                resolvedWorkspaceId,
                projectFilter: null,
                fileFilter: null,
                severityFilter: "Warning",
                diagnosticIdFilter: null,
                c).ConfigureAwait(false);
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
