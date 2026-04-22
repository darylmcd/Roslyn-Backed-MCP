using System.ComponentModel;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
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
/// Per the 2026-04-14 user preference, warming is never automatic. Callers decide when
/// (if ever) to pay the prewarm cost — the tool is experimental precisely so the shape
/// can evolve without a stability contract on the opt-in timing semantics.
/// </remarks>
[McpServerToolType]
public static class WorkspaceWarmTools
{
    [McpServerTool(Name = "workspace_warm", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("workspace", "experimental", false, false,
        "Opt-in compilation prewarm: force GetCompilationAsync + first-semantic-model resolution across the workspace to cut the cold-start penalty of the first read-side tool call."),
     Description("Opt-in compilation prewarm for a loaded workspace. Forces Roslyn's internal compilation cache and semantic-model cache to populate by calling GetCompilationAsync on every project (or the subset named by the `projects` filter) and resolving one semantic model per project. On a mid-sized solution this shifts the ~4600ms cold-start penalty of the first symbol_search / find_references call into this single explicit warm invocation, so follow-up reads run at ~40ms. Never runs automatically — callers invoke this tool explicitly after workspace_load when they want the warm-start profile. Returns ElapsedMs so callers can budget subsequent invocations; a repeat call on an unchanged workspace returns ColdCompilationCount=0 and typically ElapsedMs < 10% of the first call. No internal timeout — the caller's CancellationToken is the only cap.")]
    public static Task<string> WorkspaceWarm(
        IWorkspaceExecutionGate gate,
        IWorkspaceWarmService warmService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: project names to warm. Case-insensitive; unknown names are silently skipped. Omit or pass an empty array to warm every project in the solution.")] string[]? projects = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => warmService.WarmAsync(workspaceId, projects, c),
            ct);
}
