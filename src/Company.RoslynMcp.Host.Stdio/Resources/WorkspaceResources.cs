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

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/diagnostics", Name = "workspace_diagnostics", MimeType = "application/json")]
    [Description("Get all compiler diagnostics for a loaded workspace")]
    public static async Task<string> GetDiagnostics(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default)
    {
        var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, null, null, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(diagnostics, JsonOptions);
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}", Name = "source_file", MimeType = "text/x-csharp")]
    [Description("Read the source text of a file in the loaded workspace")]
    public static async Task<string> GetSourceFile(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        CancellationToken ct = default)
    {
        var text = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
        return text ?? $"Document not found: {filePath}";
    }
}
