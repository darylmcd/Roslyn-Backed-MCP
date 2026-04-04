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

    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get server version, capabilities, runtime information, and loaded workspace count. workspaceCount reflects sessions at call time and may briefly lag if invoked in parallel with or immediately after workspace_load; use workspace_list for authoritative session enumeration.")]
    public static Task<string> GetServerInfo(
        IWorkspaceManager workspace)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var assembly = typeof(ServerTools).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString() ?? "unknown";

            var catalogSummary = ServerSurfaceCatalog.GetSummary();
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
                        stable = catalogSummary.StableTools,
                        experimental = catalogSummary.ExperimentalTools
                    },
                    resources = new
                    {
                        stable = catalogSummary.StableResources,
                        experimental = catalogSummary.ExperimentalResources
                    },
                    prompts = new
                    {
                        stable = catalogSummary.StablePrompts,
                        experimental = catalogSummary.ExperimentalPrompts
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

            return Task.FromResult(JsonSerializer.Serialize(info, JsonDefaults.Indented));
        });
    }
}
