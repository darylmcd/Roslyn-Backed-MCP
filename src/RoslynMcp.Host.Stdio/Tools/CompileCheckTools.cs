using System.ComponentModel;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry point for the in-memory compile check. WS1 phase 1.6 — the shim
/// body delegates to <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>
/// instead of carrying the dispatch boilerplate inline. Pre-gate
/// <see cref="ParameterValidation"/> calls run synchronously before dispatch, matching
/// the pattern established in <c>BulkRefactoringTools.PreviewBulkReplaceType</c>.
/// </summary>
[McpServerToolType]
public static class CompileCheckTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "stable", true, false,
        "Fast in-memory compilation check without invoking dotnet build."),
     Description("Fast in-memory compilation check using the Roslyn Compilation API — validates compilability without invoking dotnet build. Much faster for quick feedback loops during code generation. Note: only reports compiler diagnostics (CS*); analyzer diagnostics (CA*, IDE*) are excluded — use project_diagnostics for those. The emitValidation option performs a real PE emit (not metadata-only) and is typically 50–100× slower than GetDiagnostics-only on large solutions, BUT only when the workspace has its NuGet packages restored — on a workspace with unresolved metadata references the emit phase short-circuits and the wall-clock cost matches GetDiagnostics. If you observe identical timing between emitValidation=true and emitValidation=false, run dotnet restore on the workspace first. Results are paginated — use offset/limit to page through large diagnostic sets, and severity/file filters to narrow the scope.")]
    public static Task<string> CompileCheck(
        IWorkspaceExecutionGate gate,
        ICompileCheckService compileCheckService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("When true, performs full PE-emit validation (catches more issues like missing references at emit time). Default: false (faster, uses GetDiagnostics only). Requires restored NuGet packages for the perf delta to materialize — see the tool description.")] bool emitValidation = false,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        [Description("Optional: only return diagnostics whose file path matches this absolute path")] string? file = null,
        [Description("Number of diagnostics to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum number of diagnostics to return (default: 50)")] int limit = 50,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateSeverity(severity);
        ParameterValidation.ValidatePagination(offset, limit);
        return ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => compileCheckService.CheckAsync(
                workspaceId,
                new CompileCheckOptions(projectName, emitValidation, severity, file, offset, limit),
                c),
            ct);
    }
}
