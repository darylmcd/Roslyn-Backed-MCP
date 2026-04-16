using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{

    [McpServerTool(Name = "workspace_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description("Load a .sln, .slnx, or .csproj file into the workspace for semantic analysis. Returns a lean summary by default — pass verbose=true for the full per-project tree (large solutions can produce ~30 KB or more). Idempotent by path: if the same solution/project file is already loaded in this host process, workspace_load returns the EXISTING WorkspaceId instead of creating a new one — no extra workspace slot is consumed. DocumentCount note: the per-project DocumentCount often exceeds the <Compile> item count (from evaluate_msbuild_items) by about 3 because the SDK auto-generates implicit-usings, AssemblyInfo, and GlobalUsings files that Roslyn includes in the document set but MSBuild does not list as explicit <Compile> items. Sessions persist for the lifetime of the stdio host process — there is NO inactivity TTL. A workspace can become unreachable if (a) the host process restarts (Cursor/Claude Code may relaunch the MCP server transparently between conversations), (b) workspace_close is called, or (c) the concurrent-workspace cap (ROSLYNMCP_MAX_WORKSPACES, default 8) forced an eviction. When a previously valid workspaceId returns 'Workspace was not found', call workspace_load again rather than treating it as an error.")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Load a .sln, .slnx, or .csproj into a named Roslyn workspace session.")]
    public static Task<string> LoadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln, .slnx, or .csproj file")] string path,
        [Description("When true, return the full per-project tree and workspace diagnostics. Default false returns only counts and load state.")] bool verbose = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return gate.RunLoadGateAsync(async c =>
        {
            ProgressHelper.Report(progress, 0, 1);
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, path, c).ConfigureAwait(false);
            var status = await workspace.LoadAsync(path, c);
            ProgressHelper.Report(progress, 1, 1);
            _ = NotifyResourcesChangedAsync(server);
            return verbose
                ? JsonSerializer.Serialize(status, JsonDefaults.Indented)
                : JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "workspace_reload", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Reload the currently loaded workspace to pick up file changes")]
    [McpToolMetadata("workspace", "stable", false, false,
        "Reload an existing workspace session from disk.")]
    public static Task<string> ReloadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct)
    {
        // Reload acquires both the global load gate AND the per-workspace write lock so that
        // any in-flight readers on this workspace complete before the solution is replaced.
        return gate.RunLoadGateAsync(outerCt =>
            gate.RunWriteAsync(workspaceId, async innerCt =>
            {
                var status = await workspace.ReloadAsync(workspaceId, innerCt);
                _ = NotifyResourcesChangedAsync(server);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, outerCt), ct);
    }

    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources")]
    [McpToolMetadata("workspace", "stable", false, true,
        "Close a loaded workspace session and release resources.")]
    public static Task<string> CloseWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        // Close acquires both the global load gate AND the per-workspace write lock so that
        // no reader is in flight when the workspace's lock entry is dropped from the registry.
        // RemoveGate must run after RunWriteAsync completes so the per-workspace lock entry is
        // released before being removed from the registry.
        return gate.RunLoadGateAsync(async outerCt =>
        {
            var json = await gate.RunWriteAsync(workspaceId, async innerCt =>
            {
                var closed = workspace.Close(workspaceId);
                _ = NotifyResourcesChangedAsync(server);
                return JsonSerializer.Serialize(new { success = closed, workspaceId }, JsonDefaults.Indented);
            }, outerCt).ConfigureAwait(false);
            gate.RemoveGate(workspaceId);
            return json;
        }, ct);
    }

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List all currently loaded workspace sessions. Returns a lean summary per workspace by default — pass verbose=true for the full per-project tree of every workspace.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "List active workspace sessions.")]
    public static Task<string> ListWorkspaces(
        IWorkspaceManager workspace,
        [Description("When true, return the full per-project tree and workspace diagnostics for each workspace. Default false returns only counts and load state.")] bool verbose = false)
    {
        var workspaces = workspace.ListWorkspaces();
        if (verbose)
        {
            return Task.FromResult(JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented));
        }

        var summaries = workspaces.Select(WorkspaceStatusSummaryDto.From).ToList();
        return Task.FromResult(JsonSerializer.Serialize(new { count = summaries.Count, workspaces = summaries }, JsonDefaults.Indented));
    }

    [McpServerTool(Name = "workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
        "Cheap health check after workspace_load — call this first before compile_check or heavy tools. " +
        "Default (verbose=false) returns summary JSON: isReady, isStale, workspaceErrorCount, restoreHint, solutionFileName, counts. " +
        "Pass verbose=true for the full per-project tree and workspace diagnostics.")]
    [McpToolMetadata("workspace", "stable", true, false,
        "Inspect status, diagnostics, and stale-state information for a workspace.")]
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
        "Lean workspace readiness summary (alias of workspace_status verbose=false).")]
    public static Task<string> GetWorkspaceHealth(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default) =>
        GetWorkspaceStatus(gate, workspace, workspaceId, verbose: false, ct);

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
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var documents = await workspace.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, c);
            return JsonSerializer.Serialize(documents, JsonDefaults.Indented);
        }, ct);
    }

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

            var totalLineCount = text.Count(ch => ch == '\n') + 1;
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

    /// <summary>
    /// Fire-and-forget notification to clients that the resource list has changed.
    /// </summary>
    private static async Task NotifyResourcesChangedAsync(McpServer server)
    {
        try
        {
            await server.SendNotificationAsync(NotificationMethods.ResourceListChangedNotification).ConfigureAwait(false);
        }
        catch
        {
            // Notification failure should not affect the tool result
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
