using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class WorkspaceResources
{

    [McpServerResource(UriTemplate = "roslyn://workspaces", Name = "workspaces", MimeType = "application/json")]
    [Description("List all currently loaded workspace sessions with their status")]
    public static string GetWorkspaces(IWorkspaceManager workspace)
    {
        try
        {
            var workspaces = workspace.ListWorkspaces();
            return JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented);
        }
        catch (KeyNotFoundException ex) { throw new McpToolException($"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException($"Invalid operation: {ex.Message}", ex); }
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status", Name = "workspace_status", MimeType = "application/json")]
    [Description("Get the current status of a loaded workspace including projects and diagnostics. URI uses the workspace_status template; see roslyn://server/resource-templates if your client does not list template URIs.")]
    public static string GetWorkspaceStatus(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId)
    {
        try
        {
            var status = workspace.GetStatus(workspaceId);
            return JsonSerializer.Serialize(status, JsonDefaults.Indented);
        }
        catch (KeyNotFoundException ex) { throw new McpToolException($"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException($"Invalid operation: {ex.Message}", ex); }
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/projects", Name = "workspace_projects", MimeType = "application/json")]
    [Description("Get the project dependency graph and project metadata for a workspace. Requires workspaceId from workspace_load; see roslyn://server/resource-templates for the URI pattern.")]
    public static string GetProjects(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId)
    {
        try
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            return JsonSerializer.Serialize(graph, JsonDefaults.Indented);
        }
        catch (KeyNotFoundException ex) { throw new McpToolException($"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException($"Invalid operation: {ex.Message}", ex); }
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/diagnostics", Name = "workspace_diagnostics", MimeType = "application/json")]
    [Description("Get all compiler diagnostics for a loaded workspace. Workspace-scoped URI; see roslyn://server/resource-templates.")]
    public static async Task<string> GetDiagnostics(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default)
    {
        try
        {
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, null, null, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(diagnostics, JsonDefaults.Indented);
        }
        catch (KeyNotFoundException ex) { throw new McpToolException($"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException($"Invalid operation: {ex.Message}", ex); }
    }

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}", Name = "source_file", MimeType = "text/x-csharp")]
    [Description("Read the source text of a file in the loaded workspace. filePath must be URL-encoded; see roslyn://server/resource-templates.")]
    public static async Task<string> GetSourceFile(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        CancellationToken ct = default)
    {
        try
        {
            var text = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            if (text is null)
                throw new KeyNotFoundException($"Document not found in workspace: {filePath}");
            return text;
        }
        catch (KeyNotFoundException ex) { throw new McpToolException($"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException($"Invalid operation: {ex.Message}", ex); }
    }
}
