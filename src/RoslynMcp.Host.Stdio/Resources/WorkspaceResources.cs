using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class WorkspaceResources
{

    [McpServerResource(UriTemplate = "roslyn://workspaces", Name = "workspaces", MimeType = "application/json")]
    [Description("List all currently loaded workspace sessions with a lean summary per workspace (counts and load state, no per-project tree). For the verbose payload use roslyn://workspaces/verbose. Workspace-scoped resources (status, projects, diagnostics, source_file) use URI templates — if your MCP client only shows static resources from resources/list, read roslyn://server/resource-templates to discover those templates.")]
    public static string GetWorkspaces(IWorkspaceManager workspace) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspaces", () =>
        {
            var workspaces = workspace.ListWorkspaces();
            var summaries = workspaces.Select(WorkspaceStatusSummaryDto.From).ToList();
            return JsonSerializer.Serialize(new { count = summaries.Count, workspaces = summaries }, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspaces/verbose", Name = "workspaces_verbose", MimeType = "application/json")]
    [Description("Verbose variant of roslyn://workspaces — returns the full per-project tree and workspace diagnostics for every loaded session. Can be large on multi-project solutions; prefer the summary resource at roslyn://workspaces unless you need the full payload.")]
    public static string GetWorkspacesVerbose(IWorkspaceManager workspace) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspaces/verbose", () =>
        {
            var workspaces = workspace.ListWorkspaces();
            return JsonSerializer.Serialize(new { count = workspaces.Count, workspaces }, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status", Name = "workspace_status", MimeType = "application/json")]
    [Description("Get a lean summary of a loaded workspace's status (counts and load state, no per-project tree). For the verbose payload use roslyn://workspace/{workspaceId}/status/verbose. URI uses the workspace_status template; see roslyn://server/resource-templates if your client does not list template URIs.")]
    public static string GetWorkspaceStatus(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspace/{workspaceId}/status", () =>
        {
            var status = workspace.GetStatus(workspaceId);
            return JsonSerializer.Serialize(WorkspaceStatusSummaryDto.From(status), JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/status/verbose", Name = "workspace_status_verbose", MimeType = "application/json")]
    [Description("Verbose variant of roslyn://workspace/{workspaceId}/status — returns the full per-project tree and workspace diagnostics for the workspace. Prefer the summary resource at roslyn://workspace/{workspaceId}/status unless you need the full payload.")]
    public static string GetWorkspaceStatusVerbose(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspace/{workspaceId}/status/verbose", () =>
        {
            var status = workspace.GetStatus(workspaceId);
            return JsonSerializer.Serialize(status, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/projects", Name = "workspace_projects", MimeType = "application/json")]
    [Description("Get the project dependency graph and project metadata for a workspace. Requires workspaceId from workspace_load; see roslyn://server/resource-templates for the URI pattern.")]
    public static string GetProjects(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId) =>
        ToolErrorHandler.ExecuteResource("roslyn://workspace/{workspaceId}/projects", () =>
        {
            var graph = workspace.GetProjectGraph(workspaceId);
            return JsonSerializer.Serialize(graph, JsonDefaults.Indented);
        });

    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/diagnostics", Name = "workspace_diagnostics", MimeType = "application/json")]
    [Description("Get all compiler diagnostics for a loaded workspace. Workspace-scoped URI; see roslyn://server/resource-templates.")]
    public static Task<string> GetDiagnostics(
        IDiagnosticService diagnosticService,
        [Description("The workspace session identifier")] string workspaceId,
        CancellationToken ct = default) =>
        ToolErrorHandler.ExecuteResourceAsync("roslyn://workspace/{workspaceId}/diagnostics", async () =>
        {
            var diagnostics = await diagnosticService.GetDiagnosticsAsync(workspaceId, null, null, null, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(diagnostics, JsonDefaults.Indented);
        });

    // source_file is text/x-csharp, not JSON, so we cannot return a JSON envelope on error.
    // Throwing McpToolException with the OriginatingSource keeps the framework's default
    // exception path; the MCP client will see a generic error rather than a structured envelope.
    [McpServerResource(UriTemplate = "roslyn://workspace/{workspaceId}/file/{filePath}", Name = "source_file", MimeType = "text/x-csharp")]
    [Description("Read the source text of a file in the loaded workspace. filePath must be URL-encoded; see roslyn://server/resource-templates.")]
    public static async Task<string> GetSourceFile(
        IWorkspaceManager workspace,
        [Description("The workspace session identifier")] string workspaceId,
        [Description("Absolute path to the source file (URL-encoded)")] string filePath,
        CancellationToken ct = default)
    {
        const string source = "roslyn://workspace/{workspaceId}/file/{filePath}";
        try
        {
            var text = await workspace.GetSourceTextAsync(workspaceId, filePath, ct).ConfigureAwait(false);
            if (text is null)
                throw new KeyNotFoundException($"Document not found in workspace: {filePath}");
            return text;
        }
        catch (KeyNotFoundException ex) { throw new McpToolException(source, $"Not found: {ex.Message}", ex); }
        catch (InvalidOperationException ex) { throw new McpToolException(source, $"Invalid operation: {ex.Message}", ex); }
    }
}
