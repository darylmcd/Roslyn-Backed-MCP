using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry point for opt-in compilation prewarm. Wraps
/// <see cref="IWorkspaceWarmService.WarmAsync"/> with the standard read-gated dispatch
/// (<see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>) so multiple warms run
/// concurrently on the same workspace and so an in-flight write (<c>*_apply</c>) blocks
/// the warm until it completes.
/// </summary>
/// <remarks>
/// The standalone tool remains experimental precisely so the shape can evolve without a
/// stability contract on explicit warm timing. <c>workspace_load</c> may invoke the same
/// service automatically for large solutions when the caller omits <c>prewarm</c>; callers
/// can still pass <c>prewarm: false</c> to keep a cold-load profile.
/// </remarks>
[McpServerToolType]
public static class WorkspaceWarmTools
{
    [McpServerTool(Name = "workspace_warm", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("workspace", "experimental", false, false,
        "Opt-in compilation prewarm: force GetCompilationAsync + first-semantic-model resolution across the workspace to cut the cold-start penalty of the first read-side tool call."),
     Description("Prewarm Roslyn compilations and first semantic models for all or selected projects. Use before the first read-side call; workspace_load may prewarm large solutions automatically.")]
    public static Task<string> WorkspaceWarm(
        IWorkspaceExecutionGate gate,
        IWorkspaceWarmService warmService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Optional: project names to warm. Pass as a native JSON array, not a JSON-encoded string. Example: [\"MyProject.Core\", \"MyProject.Tests\"]. Case-insensitive; unknown names are silently skipped. Omit or pass an empty array to warm every project in the solution.")] string[]? projects = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        // workspace-warm stage emissions: clients see "scheduling-warm → warming-projects →
        // done" instead of waiting silently for the ~17s P95 warm on OrchardCore-class
        // solutions. Per-project N/M is intentionally not emitted here — IWorkspaceWarmService
        // doesn't accept progress and adding it would require interface + impl edits past the
        // audit-coverage initiative scope. See ProgressHelper remarks for the label-naming
        // contract.
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ProgressHelper.ReportStage(progress, 0, 3, "scheduling-warm");
            ProgressHelper.ReportStage(progress, 1, 3, "warming-projects");
            var result = await warmService.WarmAsync(workspaceId, projects, c).ConfigureAwait(false);
            ProgressHelper.ReportStage(progress, 3, 3, "done");
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
