using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// dead-interface-member-removal-guided: composite tool that resolves an interface member,
/// confirms it has zero non-implementation callers, gathers every implementing class member,
/// and produces a single dead-code removal preview spanning the interface declaration plus
/// every implementation. Callers can then redeem the preview via <c>remove_dead_code_apply</c>.
/// </summary>
[McpServerToolType]
public static class RemoveInterfaceMemberTool
{
    [McpServerTool(Name = "remove_interface_member_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("dead-code", "experimental", true, false,
        "Composite preview removing a dead interface member and every implementation in one shot. Refuses if any external caller exists."),
     Description("Preview removing a dead interface member (method/property/event) AND every concrete implementation in one shot. Refuses to remove if the member has any non-implementation callers — returns the caller list instead. Apply via remove_dead_code_apply with the returned preview token.")]
    public static Task<string> PreviewRemoveInterfaceMember(
        IWorkspaceExecutionGate gate,
        IInterfaceMemberRemovalOrchestrator orchestrator,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Symbol handle from symbol_search/find_unused_symbols/etc. pointing at an interface method, property, or event.")] string interfaceMemberHandle,
        [Description("When true, also delete files left empty after the removal (default: false).")] bool removeEmptyFiles = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await orchestrator.PreviewRemoveAsync(workspaceId, interfaceMemberHandle, removeEmptyFiles, c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
