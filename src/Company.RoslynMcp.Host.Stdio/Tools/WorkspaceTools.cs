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
    public static async Task<string> LoadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln or .csproj file")] string path,
        CancellationToken ct)
    {
        return await gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
        {
            var status = await workspace.LoadAsync(path, c);
            return JsonSerializer.Serialize(status, JsonOptions);
        }, ct);
    }

    [McpServerTool(Name = "workspace_reload"), Description("Reload the currently loaded workspace to pick up file changes")]
    public static async Task<string> ReloadWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct)
    {
        return await gate.RunAsync(WorkspaceExecutionGate.LoadGateKey, async c =>
        {
            var status = await workspace.ReloadAsync(workspaceId, c);
            return JsonSerializer.Serialize(status, JsonOptions);
        }, ct);
    }

    [McpServerTool(Name = "workspace_status"), Description("Get the current status of the loaded workspace including projects and diagnostics")]
    public static async Task<string> GetWorkspaceStatus(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return await gate.RunAsync(workspaceId, _ =>
        {
            var status = workspace.GetStatus(workspaceId);
            return Task.FromResult(JsonSerializer.Serialize(status, JsonOptions));
        }, ct);
    }

    [McpServerTool(Name = "project_graph"), Description("Get the project dependency graph and project metadata for a loaded workspace")]
    public static async Task<string> GetProjectGraph(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return await gate.RunAsync(workspaceId, _ =>
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            return Task.FromResult(JsonSerializer.Serialize(graph, JsonOptions));
        }, ct);
    }

    [McpServerTool(Name = "source_generated_documents"), Description("List source-generated documents for a workspace or specific project")]
    public static async Task<string> GetSourceGeneratedDocuments(
        IWorkspaceExecutionGate gate,
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        CancellationToken ct = default)
    {
        return await gate.RunAsync(workspaceId, async c =>
        {
            var documents = await workspace.GetSourceGeneratedDocumentsAsync(workspaceId, projectName, c);
            return JsonSerializer.Serialize(documents, JsonOptions);
        }, ct);
    }
}
