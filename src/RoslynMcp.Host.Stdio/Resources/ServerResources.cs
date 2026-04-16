using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class ServerResources
{

    // dr-9-11-payload-exceeds-mcp-tool-result-cap: the default catalog resource returns a
    // cap-safe SUMMARY (counts + resources + workflow-hints + pagination pointers) so it
    // always fits under the MCP tool-result cap on large tool surfaces. The full tool and
    // prompt lists are served by the paginated siblings below. This is a breaking change to
    // the response shape of `roslyn://server/catalog` (Tools and Prompts lists replaced with
    // ToolCount + ToolsResourceTemplate / PromptCount + PromptsResourceTemplate); existing
    // clients that need the full catalog either paginate or switch to the full-document
    // resource `roslyn://server/catalog/full`.
    [McpServerResource(UriTemplate = "roslyn://server/catalog", Name = "server_catalog", MimeType = "application/json")]
    [Description("Machine-readable catalog summary: tool/prompt counts + resource list + workflow hints + support tiers. Tool and prompt lists are paginated via roslyn://server/catalog/tools/{offset}/{limit} and roslyn://server/catalog/prompts/{offset}/{limit}. Full unpaginated catalog at roslyn://server/catalog/full.")]
    public static string GetServerCatalog()
    {
        return JsonSerializer.Serialize(ServerSurfaceCatalog.CreateSummaryDocument(), JsonDefaults.Indented);
    }

    [McpServerResource(UriTemplate = "roslyn://server/catalog/full", Name = "server_catalog_full", MimeType = "application/json")]
    [Description("Unpaginated full server catalog including every tool and prompt entry. Large payload (~80 KB on a 168-tool surface) — may exceed the MCP tool-result cap on some clients; prefer the paginated siblings when that happens.")]
    public static string GetServerCatalogFull()
    {
        return JsonSerializer.Serialize(ServerSurfaceCatalog.CreateDocument(), JsonDefaults.Indented);
    }

    [McpServerResource(UriTemplate = "roslyn://server/catalog/tools/{offset}/{limit}", Name = "server_catalog_tools_page", MimeType = "application/json")]
    [Description("Paginated slice of the server tool catalog. offset: 0-based start index; limit: 1-200 entries per page. Response carries offset/limit/returnedCount/totalCount/hasMore + the entries array.")]
    public static string GetServerCatalogToolsPage(
        [Description("0-based start index.")] string offset,
        [Description("Entries per page (1-200).")] string limit)
    {
        return ToolErrorHandler.ExecuteResource(
            "roslyn://server/catalog/tools/{offset}/{limit}",
            () =>
            {
                var parsedOffset = ParseSlotInt(offset, nameof(offset));
                var parsedLimit = ParseSlotInt(limit, nameof(limit));
                var page = ServerSurfaceCatalog.PageEntries(
                    ServerSurfaceCatalog.Tools, parsedOffset, parsedLimit, "server_catalog_tools_page");
                return JsonSerializer.Serialize(page, JsonDefaults.Indented);
            });
    }

    [McpServerResource(UriTemplate = "roslyn://server/catalog/prompts/{offset}/{limit}", Name = "server_catalog_prompts_page", MimeType = "application/json")]
    [Description("Paginated slice of the server prompt catalog. Same shape as roslyn://server/catalog/tools/{offset}/{limit}.")]
    public static string GetServerCatalogPromptsPage(
        [Description("0-based start index.")] string offset,
        [Description("Entries per page (1-200).")] string limit)
    {
        return ToolErrorHandler.ExecuteResource(
            "roslyn://server/catalog/prompts/{offset}/{limit}",
            () =>
            {
                var parsedOffset = ParseSlotInt(offset, nameof(offset));
                var parsedLimit = ParseSlotInt(limit, nameof(limit));
                var page = ServerSurfaceCatalog.PageEntries(
                    ServerSurfaceCatalog.Prompts, parsedOffset, parsedLimit, "server_catalog_prompts_page");
                return JsonSerializer.Serialize(page, JsonDefaults.Indented);
            });
    }

    private static int ParseSlotInt(string raw, string slotName)
    {
        if (!int.TryParse(raw, out var value))
        {
            throw new ArgumentException(
                $"{slotName} must be an integer. Got: '{raw}'.",
                slotName);
        }
        return value;
    }

    [McpServerResource(UriTemplate = "roslyn://server/resource-templates", Name = "resource_templates", MimeType = "application/json")]
    [Description("Lists all supported MCP resource URI templates. Workspace-scoped resources (status, projects, diagnostics, source files) are not returned as static entries in clients that only call resources/list — substitute workspaceId (and filePath where required) using these templates.")]
    public static string GetResourceTemplates()
    {
        var templates = ServerSurfaceCatalog.Resources
            .Select(resource => new
            {
                resource.Name,
                resource.UriTemplate,
                resource.Category,
                resource.Summary,
                resource.SupportTier
            })
            .OrderBy(resource => resource.Name, StringComparer.Ordinal)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            count = templates.Count,
            resources = templates
        }, JsonDefaults.Indented);
    }
}
