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
}
