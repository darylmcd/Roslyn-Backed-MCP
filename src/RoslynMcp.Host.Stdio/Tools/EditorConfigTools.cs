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
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await editorConfigService.GetOptionsAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "set_editorconfig_option", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Set or update a key/value in the .editorconfig that applies to the given source file (under the [*.{cs,csx,cake}] section). Creates a new .editorconfig next to the file if none exists in the directory chain.")]
    public static Task<string> SetEditorConfigOption(
        IWorkspaceExecutionGate gate,
        IEditorConfigService editorConfigService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to a C# source file whose .editorconfig should be updated")] string filePath,
        [Description("EditorConfig key (e.g. dotnet_diagnostic.CA1000.severity)")] string key,
        [Description("Value (e.g. warning, suggestion, silent, none)")] string value,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunWriteAsync(workspaceId, async c =>
            {
                var result = await editorConfigService.SetOptionAsync(workspaceId, filePath, key, value, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
