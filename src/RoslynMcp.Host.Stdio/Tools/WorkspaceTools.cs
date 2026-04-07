using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{

    [McpServerTool(Name = "workspace_load", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Load a .sln, .slnx, or .csproj file into the workspace for semantic analysis. Sessions persist for the lifetime of the stdio host process — there is NO inactivity TTL. A workspace can become unreachable if (a) the host process restarts (Cursor/Claude Code may relaunch the MCP server transparently between conversations), (b) workspace_close is called, or (c) the concurrent-workspace cap (ROSLYNMCP_MAX_WORKSPACES, default 8) forced an eviction. When a previously valid workspaceId returns 'Workspace was not found', call workspace_load again rather than treating it as an error — the call is idempotent against repeated loads of the same path (BUG-010).")]
    public static Task<string> LoadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln, .slnx, or .csproj file")] string path,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(IWorkspaceExecutionGate.LoadGateKey, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, path, c).ConfigureAwait(false);
                var status = await workspace.LoadAsync(path, c);
                ProgressHelper.Report(progress, 1, 1);
                _ = NotifyResourcesChangedAsync(server);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "workspace_reload", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Reload the currently loaded workspace to pick up file changes")]
    public static Task<string> ReloadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct)
    {
        // Reload acquires both the global load gate AND the per-workspace write lock so that
        // any in-flight readers on this workspace complete before the solution is replaced.
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(IWorkspaceExecutionGate.LoadGateKey, outerCt =>
                gate.RunWriteAsync(workspaceId, async innerCt =>
                {
                    var status = await workspace.ReloadAsync(workspaceId, innerCt);
                    _ = NotifyResourcesChangedAsync(server);
                    return JsonSerializer.Serialize(status, JsonDefaults.Indented);
                }, outerCt), ct));
    }

    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources")]
    public static Task<string> CloseWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        // Close acquires both the global load gate AND the per-workspace write lock so that
        // no reader is in flight when the workspace's lock entry is dropped from the registry.
        // BUG-N2: RemoveGate must run after RunWriteAsync completes so the per-workspace semaphore
        // is released before Dispose; disposing while the writer still holds the gate caused
        // ObjectDisposedException in gate.Release().
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(IWorkspaceExecutionGate.LoadGateKey, async outerCt =>
            {
                var json = await gate.RunWriteAsync(workspaceId, async innerCt =>
                {
                    var closed = workspace.Close(workspaceId);
                    _ = NotifyResourcesChangedAsync(server);
                    return JsonSerializer.Serialize(new { success = closed, workspaceId }, JsonDefaults.Indented);
                }, outerCt).ConfigureAwait(false);
                gate.RemoveGate(workspaceId);
                return json;
            }, ct));
    }

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List all currently loaded workspace sessions")]
    public static Task<string> ListWorkspaces(
        IWorkspaceManager workspace)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var workspaces = workspace.ListWorkspaces();
            return Task.FromResult(JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented));
        });
    }

    [McpServerTool(Name = "workspace_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get the current status of the loaded workspace including projects and diagnostics")]
    public static Task<string> GetWorkspaceStatus(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var status = await workspace.GetStatusAsync(workspaceId, c).ConfigureAwait(false);
                return JsonSerializer.Serialize(status, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "project_graph", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get the project dependency graph and project metadata for a loaded workspace")]
    public static Task<string> GetProjectGraph(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, _ =>
            {
                var graph = workspace.GetProjectGraph(workspaceId);
                return Task.FromResult(JsonSerializer.Serialize(graph, JsonDefaults.Indented));
            }, ct));
    }

    [McpServerTool(Name = "source_generated_documents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List source-generated documents for a workspace or specific project")]
    public static Task<string> GetSourceGeneratedDocuments(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var documents = await workspace.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(documents, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "get_source_text", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Read the source text of a document in the loaded workspace. Returns the full text content of the file as known to the Roslyn workspace.")]
    public static Task<string> GetSourceText(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var text = await workspace.GetSourceTextAsync(workspaceId, filePath, c);
                if (text is null) throw new KeyNotFoundException($"Document not found: {filePath}");
                return JsonSerializer.Serialize(new { filePath, lineCount = text.Count(ch => ch == '\n') + 1, text }, JsonDefaults.Indented);
            }, ct));
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
}
