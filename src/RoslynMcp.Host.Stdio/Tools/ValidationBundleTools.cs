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
    /// <remarks>
    /// <para>overallStatus is one of clean | compile-error | analyzer-error | test-failure | test-zero-run | timeout.</para>
    /// <para>test-zero-run indicates runTests=true but the discovered filter matched zero tests - re-run test_run standalone against the surfaced filter; the zero-match is almost always a working-directory/filter-resolution race, not a real pass.</para>
    /// <para>timeout indicates a validation phase exceeded the 25-second internal cap; the response carries compileResult.cancelled=true plus a warnings entry naming the phase and is safe to retry.</para>
    /// <para>Pass summary=true on multi-project solutions where the default response (per-diagnostic detail plus per-test rows) exceeds the MCP cap. Pass responseFormat="markdown" for a compact summary table; the verdict is preserved across both shapes.</para>
    /// </remarks>
    [McpServerTool(Name = "validate_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "Composite post-edit validation: compile_check + project_diagnostics (errors) + test_related_files (+ optional test_run)."),
     Description("One-call post-edit validation bundle over an explicit changed-file set: in-memory compile_check, Error-severity compiler and analyzer diagnostics, related-test discovery, and optional test_run. Returns an aggregate overallStatus envelope.")]
    public static Task<string> ValidateWorkspace(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Optional: explicit list of changed file paths. Pass as a native JSON array of absolute file paths, not a JSON-encoded string. Example: [\"/abs/path/a.cs\", \"/abs/path/b.cs\"]. When omitted, the change tracker's session-wide change set is used. Empty list means no test discovery.")] string[]? changedFilePaths = null,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default: false.")] bool summary = false,
        [Description("Response shape: \"json\" (default) returns the indented JSON envelope; \"markdown\" returns a compact summary table built from the same DTO so the verdict is identical across shapes. Case-insensitive.")] string? responseFormat = null,
        CancellationToken ct = default) =>
        gate.RunReadAsync(workspaceId, async c =>
        {
            var dto = await validationService
                .ValidateAsync(workspaceId, changedFilePaths, runTests, c, summary)
                .ConfigureAwait(false);
            return RenderValidationResponse(dto, responseFormat);
        }, ct);

    /// <remarks>
    /// <para>Falls back to full-workspace scope and surfaces the fallback via the Warnings field when git is unavailable (not on PATH, solution outside a git repo, git exited non-zero).</para>
    /// <para>overallStatus is one of clean | compile-error | analyzer-error | test-failure | test-zero-run | git-status-unknown | timeout.</para>
    /// <para>git-status-unknown is unique to this tool: it fires when the git status scope-collection itself timed out, so a would-be clean verdict was computed over an untrustworthy fallback scope rather than the real working tree - retry, or raise ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS if git status is slow on this repo.</para>
    /// </remarks>
    [McpServerTool(Name = "validate_recent_git_changes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "experimental", true, false,
        "post-edit-validate-workspace-scoped-to-touched-files: auto-scoped validation companion that derives changedFilePaths from git status --porcelain, falls back to full-workspace scope with a warning when git is unavailable or the solution is outside a git repo."),
     Description("Post-edit validation bundle that auto-derives the changed-file set from `git status --porcelain` and forwards to validate_workspace. Prefer this over validate_workspace when you have uncommitted edits and want touched-file scope.")]
    public static Task<string> ValidateRecentGitChanges(
        IWorkspaceExecutionGate gate,
        IWorkspaceValidationService validationService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("When true, runs the discovered related tests via dotnet test --filter. Default: false (discovery only).")] bool runTests = false,
        [Description("When true, drops the per-diagnostic ErrorDiagnostics list and per-test DiscoveredTests list to keep the response under the MCP cap on large solutions. OverallStatus + counts still surface the verdict. Default: false.")] bool summary = false,
        CancellationToken ct = default) =>
        ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => validationService.ValidateRecentGitChangesAsync(workspaceId, runTests, c, summary),
            ct);

    /// <remarks>
    /// <para>The fork is created under .roslynmcp/forks beneath the workspace root and loaded as its own workspace.</para>
    /// <para>retention values: drop-on-success (default), drop-on-failure, drop-always, keep.</para>
    /// </remarks>
    [McpServerTool(Name = "workspace_fork_apply", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("validation", "experimental", false, false,
        "Fork a loaded workspace, replay a preview token into the fork, validate it, and retain/drop the fork by policy."),
     Description("Experimental: fork a loaded workspace, replay an existing preview token into the fork without mutating the source workspace, run validate_workspace against the fork, then retain or delete the fork per the retention policy.")]
    public static Task<string> WorkspaceForkApply(
        IWorkspaceExecutionGate gate,
        IWorkspaceForkApplyService forkApplyService,
        [Description("Source workspace session id from workspace_load.")] string workspaceId,
        [Description("Preview token from any *_preview tool.")] string previewToken,
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
