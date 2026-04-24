using System.ComponentModel;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 5 (v1.18, <c>roslyn-mcp-post-edit-validation-bundle</c>): one-call composite that
/// chains compile_check + project_diagnostics (errors) + test_related_files + optional
/// test_run. Returns a single envelope with an aggregate <c>overallStatus</c>. WS1 phase
/// 1.6 — each shim body delegates to
/// <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/> instead of carrying the
/// dispatch boilerplate inline.
/// </summary>
[McpServerToolType]
public static class ValidationBundleTools
{
    [McpServerTool(Name = "validate_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "Composite post-edit validation: compile_check + project_diagnostics (errors) + test_related_files (+ optional test_run)."),
     Description("One-call post-edit validation bundle. Runs an in-memory compile_check, harvests Error-severity compiler+analyzer diagnostics, discovers tests related to the changed file set, and (when runTests=true) executes those tests. Returns an aggregate envelope with overallStatus = clean | compile-error | analyzer-error | test-failure. Pass `summary=true` on multi-project solutions where the default response (per-diagnostic detail + per-test rows) exceeds the MCP cap (Jellyfin: 135 KB).")]
    public static Task<string> ValidateWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: explicit list of changed file paths. When omitted, the change tracker's session-wide change set is used. Empty list means no test discovery.")] string[]? changedFilePaths = null,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default false preserves the v1.18 shape.")] bool summary = false,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => validationService.ValidateAsync(workspaceId, changedFilePaths, runTests, c, summary),
            ct);

    [McpServerTool(Name = "validate_recent_git_changes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "post-edit-validate-workspace-scoped-to-touched-files: auto-scoped validation companion that derives changedFilePaths from git status --porcelain, falls back to full-workspace scope with a warning when git is unavailable or the solution is outside a git repo."),
     Description("Post-edit validation bundle that auto-derives the changed-file set from `git status --porcelain` in the solution directory, eliminating the manual path-enumeration step. Internally forwards to validate_workspace with the derived list. Falls back to full-workspace scope and surfaces the fallback via the Warnings field when git is unavailable (not on PATH, solution outside a git repo, git exited non-zero). Prefer this over validate_workspace when you have uncommitted edits and want the bundle scoped to the touched-file set.")]
    public static async Task<string> ValidateRecentGitChanges(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default false preserves the full response shape.")] bool summary = false,
        CancellationToken ct = default)
    {
        // validate-recent-git-changes-bare-error-envelope: belt-and-suspenders structured-envelope
        // wrap. The StructuredCallToolFilter already catches handler exceptions and formats them
        // via ToolErrorHandler.ClassifyAndFormat + InjectMetaIfPossible, but field reports across
        // 4 audits (self, TradeWise, NetworkDoc, FirewallAnalyzer) observed the bare SDK
        // "An error occurred invoking 'validate_recent_git_changes'." string instead of the
        // structured envelope. To guarantee the envelope regardless of filter wiring or SDK
        // transport quirks, we wrap the dispatch in an in-handler try/catch that classifies the
        // exception locally and returns the JSON envelope directly from the tool body. When the
        // filter IS active, this is a no-op pass-through on the success path; on the failure
        // path, the filter sees a well-formed JSON string (not an exception) and just injects
        // _meta. OperationCanceledException still propagates (cooperative cancellation is a
        // protocol signal, not a tool error) — matching the filter's own contract.
        try
        {
            return await ToolDispatch.ReadByWorkspaceIdAsync(
                gate,
                workspaceId,
                c => validationService.ValidateRecentGitChangesAsync(workspaceId, runTests, c, summary),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var envelope = ToolErrorHandler.ClassifyAndFormat(ex, "validate_recent_git_changes");
            return ToolErrorHandler.InjectMetaIfPossible(envelope, "validate_recent_git_changes");
        }
    }
}
