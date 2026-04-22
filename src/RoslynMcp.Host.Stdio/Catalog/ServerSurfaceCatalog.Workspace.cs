namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] WorkspaceTools =
    [
        Tool("server_info", "server", "stable", true, false, "Inspect server capabilities, versions, and support tiers."),
        Tool("server_heartbeat", "server", "stable", true, false, "Lightweight connection readiness probe — returns state/loadedWorkspaceCount/stdioPid/serverStartedAt without the full server_info payload."),
        Tool("workspace_load", "workspace", "stable", false, false, "Load a .sln, .slnx, or .csproj into a named Roslyn workspace session."),
        Tool("workspace_reload", "workspace", "stable", false, false, "Reload an existing workspace session from disk."),
        Tool("workspace_close", "workspace", "stable", false, true, "Close a loaded workspace session and release resources."),
        Tool("workspace_warm", "workspace", "experimental", false, false, "Opt-in compilation prewarm: force GetCompilationAsync + first-semantic-model resolution across the workspace to cut the cold-start penalty of the first read-side tool call."),
        Tool("workspace_list", "workspace", "stable", true, false, "List active workspace sessions."),
        Tool("workspace_status", "workspace", "stable", true, false, "Inspect status, diagnostics, and stale-state information for a workspace."),
        Tool("workspace_health", "workspace", "stable", true, false, "Lean workspace readiness summary (alias of workspace_status verbose=false)."),
        Tool("project_graph", "workspace", "stable", true, false, "Inspect project and dependency structure."),
        Tool("source_generated_documents", "workspace", "stable", true, false, "List source-generated documents for a workspace or project."),
        Tool("get_source_text", "workspace", "stable", true, false, "Read source text as the Roslyn workspace currently sees it (may differ from disk if workspace hasn't been reloaded)."),
        Tool("workspace_changes", "workspace", "stable", true, false, "List all mutations applied to a workspace during this session, with descriptions, affected files, tool names, and timestamps."),
    ];
}
