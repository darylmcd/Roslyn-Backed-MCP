using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio;

/// <summary>Concise discovery guidance returned by the MCP initialize handshake.</summary>
internal static class ServerInstructions
{
    public const int ClientCharacterLimit = 2048;

    public const string Text = """
        Roslyn MCP provides compiler-aware C#/.NET analysis, validation, and preview-first edits.
        Bootstrap: call workspace_load with the solution or project path before workspace-scoped tools. Keep its workspaceId for later calls; workspace_list and workspace_status inspect active sessions.
        Discovery: call recommend_workflow when you know the task but not the tool sequence. Use the client's MCP tool search when a deferred tool is not visible; search by capability or these categories:
        - workspace lifecycle: workspace_load, workspace_list, workspace_status, workspace_reload, workspace_close
        - navigation and analysis: symbols, definitions, references, diagnostics, flow, dependencies, metrics
        - refactoring and editing: prefer *_preview, inspect the diff, then use the documented *_apply route
        - validation: compile_check for fast in-memory checks; build_workspace/test_run for process-level parity; validate_recent_git_changes for an auto-scoped bundle
        Read-only tools may omit workspaceId only when exactly one workspace is loaded. Mutating tools require an explicit workspaceId. Prefer semantic tools over text search for C# symbols and references.
        """;

    private const string StableOnlyText = """
        Roslyn MCP provides compiler-aware C#/.NET analysis and validation.
        Bootstrap: call workspace_load with the solution or project path before workspace-scoped tools. Keep its workspaceId for later calls; workspace_list and workspace_status inspect active sessions.
        Discovery: use the client's MCP tool search when a deferred stable tool is not visible. Search by these categories:
        - workspace lifecycle: workspace_load, workspace_list, workspace_status, workspace_reload, workspace_close
        - navigation and analysis: symbols, definitions, references, diagnostics, flow, dependencies, metrics
        - validation: compile_check for fast in-memory checks; build_workspace/test_run for process-level parity
        This host is running the stable-only tool profile. Experimental workflow routing and apply routes are unavailable; use only endpoints returned by tools/list, and do not request a preview unless its compatible apply route is also listed.
        Read-only tools may omit workspaceId only when exactly one workspace is loaded. Mutating tools require an explicit workspaceId. Prefer semantic tools over text search for C# symbols and references.
        """;

    public static string For(ToolTierSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Includes("experimental") ? Text : StableOnlyText;
    }
}
