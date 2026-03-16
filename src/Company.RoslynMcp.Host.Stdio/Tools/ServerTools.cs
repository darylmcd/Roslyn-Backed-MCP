using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

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
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                roslynVersion = typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.GetName().Version?.ToString() ?? "unknown",
                workspaceCount = workspace.ListWorkspaces().Count,
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
