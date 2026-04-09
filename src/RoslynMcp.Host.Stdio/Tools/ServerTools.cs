using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ServerTools
{

    [McpServerTool(Name = "server_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get server version, capabilities, runtime information, and loaded workspace count. workspaceCount reflects sessions at call time and may briefly lag if invoked in parallel with or immediately after workspace_load; use workspace_list for authoritative session enumeration. Prompts tier note: the response carries prompts.stable and prompts.experimental counts to mirror the tool/resource tiering convention, but all currently-exposed prompts are experimental. A report like stable=0, experimental=16 is intentional and means 'no prompts have been promoted to the stable tier yet' — it is NOT a missing-surface bug.")]
    public static Task<string> GetServerInfo(
        IWorkspaceManager workspace,
        NuGetVersionChecker versionChecker)
    {
        return ToolErrorHandler.ExecuteAsync("server_info", () =>
        {
            var assembly = typeof(ServerTools).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString() ?? "unknown";

            var catalogSummary = ServerSurfaceCatalog.GetSummary();
            var wsCount = workspace.ListWorkspaces().Count;

            // Best-effort: returns cached latest version or null if pending/failed
            var latestVersion = versionChecker.GetLatestVersion();
            var currentSemver = version.Split('+')[0]; // strip git hash suffix
            var updateAvailable = latestVersion is not null
                                  && !string.Equals(currentSemver, latestVersion, StringComparison.OrdinalIgnoreCase);

            var info = new
            {
                server = "roslyn-mcp",
                version,
                productShape = "local-first",
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                roslynVersion = typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.GetName().Version?.ToString() ?? "unknown",
                workspaceCount = wsCount,
                workspaceCountHint = wsCount == 0
                    ? "If you just called workspace_load, workspaceCount may still be 0 briefly; call workspace_list for authoritative session ids."
                    : null,
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
                },
                update = latestVersion is not null ? new
                {
                    current = currentSemver,
                    latest = latestVersion,
                    updateAvailable,
                    command = updateAvailable ? "dotnet tool update -g Darylmcd.RoslynMcp" : (string?)null
                } : null
            };

            return Task.FromResult(JsonSerializer.Serialize(info, JsonDefaults.Indented));
        });
    }
}
