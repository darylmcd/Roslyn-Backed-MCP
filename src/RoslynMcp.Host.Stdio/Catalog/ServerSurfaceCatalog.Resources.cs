namespace RoslynMcp.Host.Stdio.Catalog;

public static partial class ServerSurfaceCatalog
{
    private static readonly SurfaceEntry[] ServerResources =
    [
        Resource("server_catalog", "server", "stable", true, false, "Cap-safe summary of the server surface (tool/prompt counts + resource list + workflow hints). Full tools and prompts via paginated siblings.", "roslyn://server/catalog"),
        Resource("server_catalog_full", "server", "experimental", true, false, "Unpaginated full catalog including every tool and prompt entry. Large payload (~80 KB).", "roslyn://server/catalog/full"),
        Resource("server_catalog_tools_page", "server", "experimental", true, false, "Paginated slice of the server tool catalog. Slots: offset (0-based) + limit (1-200).", "roslyn://server/catalog/tools/{offset}/{limit}"),
        Resource("server_catalog_prompts_page", "server", "experimental", true, false, "Paginated slice of the server prompt catalog. Slots: offset (0-based) + limit (1-200).", "roslyn://server/catalog/prompts/{offset}/{limit}"),
        Resource("resource_templates", "server", "stable", true, false, "Lists all resource URI templates, including workspace-scoped templates.", "roslyn://server/resource-templates"),
        Resource("workspaces", "workspace", "stable", true, false, "List active workspace sessions (lean summary; counts and load state, no per-project tree).", "roslyn://workspaces"),
        Resource("workspaces_verbose", "workspace", "stable", true, false, "List active workspace sessions with full per-project tree and diagnostics.", "roslyn://workspaces/verbose"),
        Resource("workspace_status", "workspace", "stable", true, false, "Inspect workspace status (lean summary; counts and load state, no per-project tree).", "roslyn://workspace/{workspaceId}/status"),
        Resource("workspace_status_verbose", "workspace", "stable", true, false, "Inspect workspace status with full per-project tree and workspace diagnostics.", "roslyn://workspace/{workspaceId}/status/verbose"),
        Resource("workspace_projects", "workspace", "stable", true, false, "Read project graph metadata for a workspace.", "roslyn://workspace/{workspaceId}/projects"),
        Resource("workspace_diagnostics", "analysis", "stable", true, false, "Read all compiler diagnostics for a workspace.", "roslyn://workspace/{workspaceId}/diagnostics"),
        Resource("source_file", "workspace", "stable", true, false, "Read a source file from the loaded workspace.", "roslyn://workspace/{workspaceId}/file/{filePath}"),
        Resource("source_file_lines", "workspace", "experimental", true, false, "Read a 1-based inclusive line range from a source file in the loaded workspace.", "roslyn://workspace/{workspaceId}/file/{filePath}/lines/{lineRange}")
    ];

    public static IReadOnlyList<SurfaceEntry> Resources => ServerResources;
}
