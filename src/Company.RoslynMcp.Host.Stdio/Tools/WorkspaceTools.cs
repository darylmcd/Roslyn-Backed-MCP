using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using Company.RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "workspace_load"), Description("Load a .sln or .csproj file into the workspace for semantic analysis")]
    public static Task<string> LoadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln or .csproj file")] string path,
        CancellationToken ct)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
            {
                var status = await workspace.LoadAsync(path, c);
                return JsonSerializer.Serialize(status, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "workspace_reload"), Description("Reload the currently loaded workspace to pick up file changes")]
    public static Task<string> ReloadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
            {
                var status = await workspace.ReloadAsync(workspaceId, c);
                return JsonSerializer.Serialize(status, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "workspace_close"), Description("Close and dispose a loaded workspace session, freeing all resources")]
    public static Task<string> CloseWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, _ =>
            {
                var closed = workspace.Close(workspaceId);
                if (gate is WorkspaceExecutionGate concreteGate)
                {
                    concreteGate.RemoveGate(workspaceId);
                }
                return Task.FromResult(JsonSerializer.Serialize(new { success = closed, workspaceId }, JsonOptions));
            }, ct));
    }

    [McpServerTool(Name = "workspace_list"), Description("List all currently loaded workspace sessions")]
    public static Task<string> ListWorkspaces(
        IWorkspaceManager workspace)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var workspaces = workspace.ListWorkspaces();
            return Task.FromResult(JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonOptions));
        });
    }

    [McpServerTool(Name = "workspace_status"), Description("Get the current status of the loaded workspace including projects and diagnostics")]
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

    [McpServerTool(Name = "project_graph"), Description("Get the project dependency graph and project metadata for a loaded workspace")]
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

    [McpServerTool(Name = "source_generated_documents"), Description("List source-generated documents for a workspace or specific project")]
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

    [McpServerTool(Name = "get_source_text"), Description("Read the source text of a document in the loaded workspace. Returns the full text content of the file as known to the Roslyn workspace.")]
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
                if (text is null) return JsonSerializer.Serialize(new { error = $"Document not found: {filePath}" }, JsonOptions);
                return JsonSerializer.Serialize(new { filePath, lineCount = text.Count(ch => ch == '\n') + 1, text }, JsonOptions);
            }, ct));
    }
}
