using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class WorkspaceResources
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerResource(UriTemplate = "roslyn://workspaces", Name = "workspaces", MimeType = "application/json")]
    [Description("List all currently loaded workspace sessions with their status")]
    public static string GetWorkspaces(IWorkspaceManager workspace)
    {
        var workspaces = workspace.ListWorkspaces();
        return JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonOptions);
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status", Name = "workspace_status", MimeType = "application/json")]
    [Description("Get the current status of a loaded workspace including projects and diagnostics")]
    public static string GetWorkspaceStatus(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId)
    {
        var status = workspace.GetStatus(workspaceId);
        return JsonSerializer.Serialize(status, JsonOptions);
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/projects", Name = "workspace_projects", MimeType = "application/json")]
    [Description("Get the project dependency graph and project metadata for a workspace")]
    public static string GetProjects(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId)
    {
        var graph = workspace.GetProjectGraph(workspaceId);
        return JsonSerializer.Serialize(graph, JsonOptions);
    }
}
