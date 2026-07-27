using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Formatters;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Composite validation and isolated-fork MCP tool wrappers.
/// </summary>
[McpServerToolType]
public static class ValidationBundleTools
{
    [McpServerTool(Name = "validate_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "Composite post-edit validation: compile_check + project_diagnostics (errors) + test_related_files (+ optional test_run)."),
     Description("One-call post-edit validation bundle. Runs an in-memory compile_check, harvests Error-severity compiler+analyzer diagnostics, discovers tests related to the changed file set, and (when runTests=true) executes those tests. Returns an aggregate envelope with overallStatus = clean | compile-error | analyzer-error | test-failure | timeout. `timeout` indicates a validation phase exceeded the 25-second internal cap; the response carries `compileResult.cancelled=true` plus a `warnings` entry naming the phase and is safe to retry. Pass `summary=true` on multi-project solutions where the default response (per-diagnostic detail + per-test rows) exceeds the MCP cap (Jellyfin: 135 KB). Pass `responseFormat=\"markdown\"` for a compact ~30-line summary table when the JSON envelope is overkill — verdict (overallStatus) is preserved across both shapes.")]
    public static Task<string> ValidateWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: explicit list of changed file paths. Pass as a native JSON array of absolute file paths, not a JSON-encoded string. Example: [\"/abs/path/a.cs\", \"/abs/path/b.cs\"]. When omitted, the change tracker's session-wide change set is used. Empty list means no test discovery.")] string[]? changedFilePaths = null,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default false preserves the v1.18 shape.")] bool summary = false,
        [Description("Response shape: \"json\" (default) returns the indented JSON envelope; \"markdown\" returns a compact summary table built from the same DTO so the verdict is identical across shapes. Case-insensitive.")] string? responseFormat = null,
        CancellationToken ct = default) =>
        gate.RunReadAsync(workspaceId, async c =>
        {
            var dto = await validationService
                .ValidateAsync(workspaceId, changedFilePaths, runTests, c, summary)
                .ConfigureAwait(false);
            return RenderValidationResponse(dto, responseFormat);
        }, ct);

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

    [McpServerTool(Name = "workspace_fork_apply", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "experimental", false, false,
        "Fork a loaded workspace, replay a preview token into the fork, validate it, and retain/drop the fork by policy."),
     Description("Experimental validation-bundle tool. Creates a server-owned fork under <workspace>/.roslynmcp/forks, replays an existing preview token into that fork without mutating the source workspace, loads the fork as a workspace, runs validate_workspace, and retains or deletes the fork according to retention. retention values: drop-on-success (default), drop-on-failure, drop-always, keep.")]
    public static Task<string> WorkspaceForkApply(
        IWorkspaceExecutionGate gate,
        IWorkspaceForkApplyService forkApplyService,
        [Description("The source workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("The preview token returned by an *_preview tool for the source workspace")] string previewToken,
        [Description("Fork retention policy: drop-on-success, drop-on-failure, drop-always, or keep. Default: drop-on-success.")] string retention = "drop-on-success",
        [Description("When true, runs related tests discovered by validate_workspace. If testFilter is supplied, runs that explicit filter instead.")] bool runTests = false,
        [Description("Optional explicit dotnet test filter to run against the fork when runTests=true.")] string? testFilter = null,
        [Description("Optional stable suffix for the fork directory. Sanitized by the server.")] string? forkName = null,
        CancellationToken ct = default) =>
        gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await forkApplyService.ApplyAsync(
                workspaceId,
                previewToken,
                retention,
                runTests,
                testFilter,
                forkName,
                c).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct, applyStalenessPolicy: false);

    internal static string RenderValidationResponse(WorkspaceValidationDto dto, string? responseFormat)
    {
        if (!string.IsNullOrWhiteSpace(responseFormat)
            && string.Equals(responseFormat, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateWorkspaceMarkdownFormatter.Format(dto);
        }

        return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
    }
}
