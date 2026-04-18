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
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await suppressionService.SetDiagnosticSeverityAsync(
                workspaceId, diagnosticId, severity, filePath, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
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
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await suppressionService.AddPragmaWarningDisableAsync(
                workspaceId, filePath, line, diagnosticId, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "verify_pragma_suppresses", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "stable", true, false,
        "Verify an existing #pragma warning disable/restore pair covers a fire line."),
     Description("Check whether a '#pragma warning disable/restore' pair for the given diagnostic id actually covers the specified 1-based line. Detects 'cosmetic pragma' bugs where the pragma pair wraps the wrong span (e.g. pair wraps line 68 but the diagnostic actually fires at line 78). Read-only — no edits.")]
    public static Task<string> VerifyPragmaSuppresses(
        IWorkspaceExecutionGate gate,
        ISuppressionService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the C# source file to inspect")] string filePath,
        [Description("1-based line number that should be covered (the diagnostic fire site)")] int line,
        [Description("Diagnostic id whose suppression to check (e.g. CA2025)")] string diagnosticId,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await suppressionService.VerifyPragmaSuppressesAsync(
                workspaceId, filePath, line, diagnosticId, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "pragma_scope_widen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, false,
        "Extend an existing #pragma warning restore past a target line."),
     Description("Extend a matching '#pragma warning restore &lt;id&gt;' down to cover a previously-uncovered fire site. Refuses the edit when relocating the restore would cross a #region/#endregion boundary or nest into another '#pragma warning disable &lt;id&gt;' for the same id (both would silently change the effective scope of other suppressions). Idempotent no-op when the existing pair already covers the target.")]
    public static Task<string> PragmaScopeWiden(
        IWorkspaceExecutionGate gate,
        ISuppressionService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the C# source file to modify")] string filePath,
        [Description("1-based line number that must be covered after the widen (the diagnostic fire site)")] int line,
        [Description("Diagnostic id whose 'restore' is being moved (e.g. CA2025)")] string diagnosticId,
        CancellationToken ct = default)
    {
        return gate.RunWriteAsync(workspaceId, async c =>
        {
            var result = await suppressionService.WidenPragmaScopeAsync(
                workspaceId, filePath, line, diagnosticId, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
