using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class CompileCheckTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Fast in-memory compilation check using the Roslyn Compilation API — validates compilability without invoking dotnet build. Much faster for quick feedback loops during code generation. Note: only reports compiler diagnostics (CS*); analyzer diagnostics (CA*, IDE*) are excluded — use project_diagnostics for those. The emitValidation option is often much slower than GetDiagnostics-only (frequently 50–100x or more on large solutions) but catches emit-time issues.")]
    public static Task<string> CompileCheck(
        IWorkspaceExecutionGate gate,
        ICompileCheckService compileCheckService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("When true, performs full emit validation (catches more issues like missing references at emit time). Default: false (faster, uses GetDiagnostics only).")] bool emitValidation = false,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await compileCheckService.CheckAsync(workspaceId, project, emitValidation, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
