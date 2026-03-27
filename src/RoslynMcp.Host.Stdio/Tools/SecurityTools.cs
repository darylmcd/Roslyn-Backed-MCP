using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SecurityTools
{

    [McpServerTool(Name = "security_diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get security-relevant diagnostics with OWASP categorization, severity classification, and fix hints. Filters the existing diagnostic pipeline to return only security findings.")]
    public static Task<string> GetSecurityDiagnostics(
        IWorkspaceExecutionGate gate,
        ISecurityDiagnosticService securityService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? project = null,
        [Description("Optional: filter by file path")] string? file = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var results = await securityService.GetSecurityDiagnosticsAsync(workspaceId, project, file, c);
                return JsonSerializer.Serialize(results, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "security_analyzer_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Check whether security analyzer packages are present in the workspace. Returns which analyzers are installed and recommends missing packages for improved security coverage.")]
    public static Task<string> GetSecurityAnalyzerStatus(
        IWorkspaceExecutionGate gate,
        ISecurityDiagnosticService securityService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await securityService.GetAnalyzerStatusAsync(workspaceId, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
