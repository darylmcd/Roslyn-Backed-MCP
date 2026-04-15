using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SuppressionTools
{
    [McpServerTool(Name = "set_diagnostic_severity", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("configuration", "stable", false, false,
        "Set dotnet_diagnostic severity in .editorconfig."),
     Description("Set dotnet_diagnostic.&lt;id&gt;.severity in .editorconfig for C# files (warning, suggestion, silent, none), scoped from a source file path.")]
    public static Task<string> SetDiagnosticSeverity(
        IWorkspaceExecutionGate gate,
        ISuppressionService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic id (e.g. CA1000, CS8602)")] string diagnosticId,
        [Description("Severity: error, warning, suggestion, silent, or none")] string severity,
        [Description("(required) Absolute path to any C# file used to locate the applicable .editorconfig. Without this the server can't pick which .editorconfig to mutate.")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("set_diagnostic_severity", () =>
            gate.RunWriteAsync(workspaceId, async c =>
            {
                var result = await suppressionService.SetDiagnosticSeverityAsync(
                    workspaceId, diagnosticId, severity, filePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "add_pragma_suppression", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, false,
        "Insert a #pragma warning disable before a line."),
     Description("Insert #pragma warning disable &lt;id&gt; immediately before the given 1-based line in a source file.")]
    public static Task<string> AddPragmaSuppression(
        IWorkspaceExecutionGate gate,
        ISuppressionService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number: pragma is inserted before this line")] int line,
        [Description("Diagnostic id (e.g. CS0168)")] string diagnosticId,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("add_pragma_suppression", () =>
            gate.RunWriteAsync(workspaceId, async c =>
            {
                var result = await suppressionService.AddPragmaWarningDisableAsync(
                    workspaceId, filePath, line, diagnosticId, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
