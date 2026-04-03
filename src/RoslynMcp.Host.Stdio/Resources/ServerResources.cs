using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Resources;

[McpServerResourceType]
public static class ServerResources
{

    [McpServerResource(UriTemplate = "roslyn://server/catalog", Name = "server_catalog", MimeType = "application/json")]
    [Description("Machine-readable catalog of tools, resources, prompts, support tiers, and product boundaries.")]
    public static string GetServerCatalog()
    {
        return JsonSerializer.Serialize(ServerSurfaceCatalog.CreateDocument(), JsonDefaults.Indented);
    }

    [McpServerResource(UriTemplate = "roslyn://server/resource-templates", Name = "resource_templates", MimeType = "application/json")]
    [Description("Lists all supported MCP resource URI templates, including workspace-scoped templates.")]
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
