using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 5 (v1.18, <c>roslyn-mcp-post-edit-validation-bundle</c>): one-call composite that
/// chains compile_check + project_diagnostics (errors) + test_related_files + optional
/// test_run. Returns a single envelope with an aggregate <c>overallStatus</c>.
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
    {
        return ToolErrorHandler.ExecuteAsync("validate_workspace", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var dto = await validationService
                    .ValidateAsync(workspaceId, changedFilePaths, runTests, c, summary)
                    .ConfigureAwait(false);
                return JsonSerializer.Serialize(dto, JsonDefaults.Indented);
            }, ct));
    }
}
