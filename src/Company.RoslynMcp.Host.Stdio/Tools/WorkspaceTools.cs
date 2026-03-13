using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class WorkspaceTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "workspace_load"), Description("Load a .sln or .csproj file into the workspace for semantic analysis")]
    public static async Task<string> LoadWorkspace(
        IWorkspaceManager workspace,
        [Description("Absolute path to a .sln or .csproj file")] string path,
        CancellationToken ct)
    {
        var status = await workspace.LoadAsync(path, ct);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    [McpServerTool(Name = "workspace_reload"), Description("Reload the currently loaded workspace to pick up file changes")]
    public static async Task<string> ReloadWorkspace(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct)
    {
        var status = await workspace.ReloadAsync(workspaceId, ct);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    [McpServerTool(Name = "workspace_status"), Description("Get the current status of the loaded workspace including projects and diagnostics")]
    public static string GetWorkspaceStatus(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId)
    {
        var status = workspace.GetStatus(workspaceId);
        return JsonSerializer.Serialize(status, JsonOptions);
    }
}
