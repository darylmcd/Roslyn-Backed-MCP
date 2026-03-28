using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class EditorConfigTools
{
    [McpServerTool(Name = "get_editorconfig_options", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the effective .editorconfig options for a source file — returns the active code style rules, formatting settings, and naming conventions that Roslyn applies to the file.")]
    public static Task<string> GetEditorConfigOptions(
        IWorkspaceExecutionGate gate,
        IEditorConfigService editorConfigService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to inspect")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await editorConfigService.GetOptionsAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
