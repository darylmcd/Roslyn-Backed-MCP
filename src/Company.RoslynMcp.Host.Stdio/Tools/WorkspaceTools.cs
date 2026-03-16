using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "workspace_load", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description("Load a .sln or .csproj file into the workspace for semantic analysis")]
    public static Task<string> LoadWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln or .csproj file")] string path,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
            {
                ProgressHelper.Report(progress, 0, 1);
                await ValidatePathAgainstRootsAsync(server, path, c).ConfigureAwait(false);
                var status = await workspace.LoadAsync(path, c);
                ProgressHelper.Report(progress, 1, 1);
                _ = NotifyResourcesChangedAsync(server);
                return JsonSerializer.Serialize(status, JsonOptions);
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
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
            {
                var status = await workspace.ReloadAsync(workspaceId, c);
                _ = NotifyResourcesChangedAsync(server);
                return JsonSerializer.Serialize(status, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "workspace_close", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Close and dispose a loaded workspace session, freeing all resources")]
    public static Task<string> CloseWorkspace(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, c =>
            {
                var closed = workspace.Close(workspaceId);
                if (gate is WorkspaceExecutionGate concreteGate)
                {
                    concreteGate.RemoveGate(workspaceId);
                }
                _ = NotifyResourcesChangedAsync(server);
                return Task.FromResult(JsonSerializer.Serialize(new { success = closed, workspaceId }, JsonOptions));
            }, ct));
    }

    [McpServerTool(Name = "workspace_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("List all currently loaded workspace sessions")]
    public static Task<string> ListWorkspaces(
        IWorkspaceManager workspace)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var workspaces = workspace.ListWorkspaces();
            return Task.FromResult(JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonOptions));
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
            gate.RunAsync(workspaceId, _ =>
            {
                var status = workspace.GetStatus(workspaceId);
                return Task.FromResult(JsonSerializer.Serialize(status, JsonOptions));
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
            gate.RunAsync(workspaceId, _ =>
            {
                var graph = workspace.GetProjectGraph(workspaceId);
                return Task.FromResult(JsonSerializer.Serialize(graph, JsonOptions));
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
            gate.RunAsync(workspaceId, async c =>
            {
                var documents = await workspace.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, c);
                return JsonSerializer.Serialize(documents, JsonOptions);
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
            gate.RunAsync(workspaceId, async c =>
            {
                var text = await workspace.GetSourceTextAsync(workspaceId, filePath, c);
                if (text is null) throw new KeyNotFoundException($"Document not found: {filePath}");
                return JsonSerializer.Serialize(new { filePath, lineCount = text.Count(ch => ch == '\n') + 1, text }, JsonOptions);
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

    /// <summary>
    /// If the client advertises roots, validate that the requested path falls under an allowed root.
    /// If the client doesn't support roots, this is a no-op.
    /// </summary>
    private static async Task ValidatePathAgainstRootsAsync(McpServer server, string path, CancellationToken ct)
    {
        try
        {
            if (server.ClientCapabilities?.Roots is null) return;

            var rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), ct).ConfigureAwait(false);
            if (rootsResult.Roots.Count == 0) return;

            var fullPath = Path.GetFullPath(path);
            foreach (var root in rootsResult.Roots)
            {
                if (root.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var rootPath = new Uri(root.Uri).LocalPath;
                    if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            throw new ArgumentException(
                $"Path '{path}' is not under any client-sanctioned root. " +
                $"Allowed roots: {string.Join(", ", rootsResult.Roots.Select(r => r.Uri))}");
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch
        {
            // If roots request fails (e.g., client doesn't actually support it), allow the operation
        }
    }
}
