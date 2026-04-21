using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for security diagnostics and NuGet vulnerability scanning.
/// WS1 phase 1.6 — <c>GetSecurityDiagnostics</c> and <c>GetSecurityAnalyzerStatus</c>
/// (pure dispatch) delegate to <see cref="ToolDispatch.ReadByWorkspaceIdAsync{TDto}"/>;
/// <c>ScanNuGetVulnerabilities</c> keeps its hand-written body because it wraps
/// <see cref="ProgressHelper.Report"/> calls around the service call (non-dispatch
/// bookkeeping).
/// </summary>
[McpServerToolType]
public static class SecurityTools
{

    [McpServerTool(Name = "security_diagnostics", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Get security-relevant diagnostics with OWASP categorization, severity classification, and fix hints. Filters the existing diagnostic pipeline to return only security findings.")]
    [McpToolMetadata("security", "stable", true, false,
        "Return security-relevant diagnostics with OWASP categorization and fix hints.")]
    public static Task<string> GetSecurityDiagnostics(
        IWorkspaceExecutionGate gate,
        ISecurityDiagnosticService securityService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: filter by project name")] string? projectName = null,
        [Description("Optional: filter by file path")] string? file = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => securityService.GetSecurityDiagnosticsAsync(workspaceId, projectName, file, c),
            ct);

    [McpServerTool(Name = "security_analyzer_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description("Check whether security analyzer packages are present in the workspace. Returns which analyzers are installed and recommends missing packages for improved security coverage.")]
    [McpToolMetadata("security", "stable", true, false,
        "Check which security analyzer packages are present and recommend missing ones.")]
    public static Task<string> GetSecurityAnalyzerStatus(
        IWorkspaceExecutionGate gate,
        ISecurityDiagnosticService securityService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => securityService.GetAnalyzerStatusAsync(workspaceId, c),
            ct);

    [McpServerTool(Name = "nuget_vulnerability_scan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("security", "stable", true, false,
        "Scan NuGet references for known CVEs using dotnet list package --vulnerable."),
     Description("Scan NuGet package references for known security vulnerabilities (CVEs) using the NuGet vulnerability database via dotnet list package. Returns affected packages with severity, advisory links, and project locations. Response includes IncludesTransitive: when false, results match direct references only — use includeTransitive=true (and CLI transitive flags) when you need full transitive CVE coverage. Requires .NET 8+ SDK with JSON output support.")]
    public static Task<string> ScanNuGetVulnerabilities(
        IWorkspaceExecutionGate gate,
        INuGetDependencyService nuGetDependencyService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Optional: restrict scan to a specific project name")] string? projectName = null,
        [Description("Include transitive (indirect) dependencies in the scan. Default: false")] bool includeTransitive = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            ProgressHelper.Report(progress, 0, 1);
            var result = await nuGetDependencyService.ScanNuGetVulnerabilitiesAsync(workspaceId, projectName, includeTransitive, c);
            ProgressHelper.Report(progress, 1, 1);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
