using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ServerTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get server version, capabilities, runtime information, and loaded workspace count")]
    public static Task<string> GetServerInfo(
        IWorkspaceManager workspace)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var assembly = typeof(ServerTools).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString() ?? "unknown";

            var info = new
            {
                server = "roslyn-mcp",
                version,
                productShape = "local-first",
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                roslynVersion = typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.GetName().Version?.ToString() ?? "unknown",
                workspaceCount = workspace.ListWorkspaces().Count,
                catalogVersion = ServerSurfaceCatalog.CatalogVersion,
                surface = new
                {
                    tools = new
                    {
                        stable = ServerSurfaceCatalog.GetSummary().StableTools,
                        experimental = ServerSurfaceCatalog.GetSummary().ExperimentalTools
                    },
                    resources = new
                    {
                        stable = ServerSurfaceCatalog.GetSummary().StableResources,
                        experimental = ServerSurfaceCatalog.GetSummary().ExperimentalResources
                    },
                    prompts = new
                    {
                        stable = ServerSurfaceCatalog.GetSummary().StablePrompts,
                        experimental = ServerSurfaceCatalog.GetSummary().ExperimentalPrompts
                    }
                },
                productBoundaries = new[]
                {
                    "Stable support targets the local stdio host on a developer workstation.",
                    "Workspace state comes from on-disk MSBuildWorkspace snapshots rather than unsaved editor buffers.",
                    "Remote HTTP/SSE hosting is not part of the current stable release contract."
                },
                capabilities = new
                {
                    tools = true,
                    resources = true,
                    prompts = true,
                    logging = true,
                    progress = true
                }
            };

            return Task.FromResult(JsonSerializer.Serialize(info, JsonOptions));
        });
    }
}
