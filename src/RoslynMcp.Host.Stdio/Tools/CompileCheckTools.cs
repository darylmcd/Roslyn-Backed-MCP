using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;

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
    /// <remarks>
    /// <para>The emitValidation option performs a real PE emit (not metadata-only) and is typically 50-100x slower than GetDiagnostics-only on large solutions, BUT only when the workspace has its NuGet packages restored - on a workspace with unresolved metadata references the emit phase short-circuits and the wall-clock cost matches GetDiagnostics. If you observe identical timing between emitValidation=true and emitValidation=false, run dotnet restore on the workspace first.</para>
    /// <para>Use offset/limit to page through large diagnostic sets.</para>
    /// <para>When file/files resolve to documents owned by one project, compilation is scoped to that project; unresolved or multi-project file filters fall back to the requested project scope or the full solution and surface the fallback in restoreHint. The response also carries structured requestedScope/actualScope fields (one of files, project, or solution) so widening can be detected programmatically - requestedScope != actualScope means the supplied file scope was not honoured - without parsing restoreHint prose.</para>
    /// </remarks>
    [McpServerTool(Name = "compile_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "stable", true, false,
        "Fast in-memory compilation check without invoking dotnet build."),
     Description("Fast in-memory compilation check using the Roslyn Compilation API - validates compilability without invoking dotnet build. Reports compiler diagnostics (CS*) only; use project_diagnostics for analyzer (CA*, IDE*) diagnostics. Results are paginated.")]
    public static Task<string> CompileCheck(
        IWorkspaceExecutionGate gate,
        ICompileCheckService compileCheckService,
        [Description("Optional: the workspace session identifier returned by workspace_load. With one workspace loaded you may omit it — the read-path middleware resolves it automatically; pass it explicitly when two or more are loaded.")] string? workspaceId = null,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("When true, performs full PE-emit validation (catches more issues like missing references at emit time). Default: false (faster, uses GetDiagnostics only). Requires restored NuGet packages for the perf delta to materialize — see the tool description.")] bool emitValidation = false,
        [Description("Optional: minimum severity filter (Error, Warning, Info, Hidden)")] string? severity = null,
        [Description("Optional: only return diagnostics whose file path matches this absolute path")] string? file = null,
        [Description("Optional: only return diagnostics whose file path matches any absolute path in this list. Pass as a native JSON array of absolute file paths, not a JSON-encoded string. When combined with file, the union is used.")] string[]? files = null,
        [Description("Number of diagnostics to skip before returning results (default: 0)")] int offset = 0,
        [Description("Maximum number of diagnostics to return (default: 50)")] int limit = 50,
        IWorkspaceManager? workspaceManager = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidateSeverity(severity);
        ParameterValidation.ValidatePagination(offset, limit);
        workspaceId = ToolDispatch.RequireResolvedWorkspaceId(workspaceId);
        // workspace-eviction-no-auto-retry-on-tool-call: route through the eviction-tolerant
        // dispatch helper so a workspace evicted by MaxConcurrentWorkspaces pressure is
        // rehydrated from its recorded LoadedPath and the check retried once, transparently.
        // workspaceManager is DI-bound (never surfaced as a tool input); when absent — direct
        // C# callers in tests — the helper degrades to the plain non-retrying dispatch.
        return ToolDispatch.ReadByWorkspaceIdWithEvictionRetryAsync(
            gate,
            workspaceManager,
            workspaceId,
            async (wsId, c) =>
            {
                var result = await compileCheckService.CheckAsync(
                    wsId,
                    new CompileCheckOptions(projectName, emitValidation, severity, file, offset, limit, files),
                    c).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(projectName) && result.TotalProjects == 0)
                {
                    throw new ArgumentException(
                        $"projectName '{projectName}' matched 0 projects. Omit projectName or use workspace_status to inspect available project names.",
                        nameof(projectName));
                }

                return result;
            },
            ct,
            loggerFactory);
    }

}
