using System.ComponentModel;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for .editorconfig inspection and mutation. WS1 phase 1.5 —
/// each shim body delegates to the corresponding <see cref="ToolDispatch"/> helper
/// instead of carrying the 5–7 line dispatch boilerplate inline. See
/// <c>CodeActionTools</c> (canary, PR #305), phases 1.3/1.4, and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the migration
/// rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class EditorConfigTools
{
    [McpServerTool(Name = "get_editorconfig_options", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("configuration", "stable", true, false,
        "Get effective .editorconfig options for a source file."),
     Description("Get the effective .editorconfig options for a source file — returns the active code style rules, formatting settings, and naming conventions that Roslyn applies to the file.")]
    public static Task<string> GetEditorConfigOptions(
        IWorkspaceExecutionGate gate,
        IEditorConfigService editorConfigService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file to inspect")] string filePath,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => editorConfigService.GetOptionsAsync(workspaceId, filePath, c),
            ct);

    [McpServerTool(Name = "set_editorconfig_option", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("configuration", "stable", false, false,
        "Set or update a key in .editorconfig for C# files (creates file if needed)."),
     Description("Set or update a key/value in the .editorconfig that applies to the given source file (under the [*.{cs,csx,cake}] section). Creates a new .editorconfig next to the file if none exists in the directory chain. Direct-apply (no preview token), but integrated with revert_last_apply: the pre-write .editorconfig content is captured and restored on revert (or the file is deleted on revert if this call created it). Read the existing settings first with get_editorconfig_options so you can confirm the change you intend to make before calling this tool.")]
    public static Task<string> SetEditorConfigOption(
        IWorkspaceExecutionGate gate,
        IEditorConfigService editorConfigService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to a C# source file whose .editorconfig should be updated")] string filePath,
        [Description("EditorConfig key (e.g. dotnet_diagnostic.CA1000.severity)")] string key,
        [Description("Value (e.g. warning, suggestion, silent, none)")] string value,
        CancellationToken ct = default)
        => ToolDispatch.PreviewWithWorkspaceIdAsync(
            gate,
            workspaceId,
            c => editorConfigService.SetOptionAsync(workspaceId, filePath, key, value, c),
            ct);
}
