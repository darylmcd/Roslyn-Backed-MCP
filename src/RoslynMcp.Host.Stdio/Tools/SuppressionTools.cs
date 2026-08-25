using System.ComponentModel;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for diagnostic-severity and pragma-suppression operations.
/// WS1 phase 1.5 — each shim body delegates to the corresponding
/// <see cref="ToolDispatch"/> helper instead of carrying the dispatch boilerplate
/// inline.
/// </summary>
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
        => ToolDispatch.PreviewWithWorkspaceIdAsync(
            gate,
            workspaceId,
            c => suppressionService.SetDiagnosticSeverityAsync(
                workspaceId, diagnosticId, severity, filePath, c),
            ct);

    [McpServerTool(Name = "add_pragma_suppression", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, false,
        "Insert a #pragma warning disable before a line."),
     Description("Insert #pragma warning disable &lt;id&gt; immediately before the given 1-based line in a source file.")]
    public static Task<string> AddPragmaSuppression(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IPinnedSuppressionWriteService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number: pragma is inserted before this line")] int line,
        [Description("Diagnostic id (e.g. CS0168)")] string diagnosticId,
        CancellationToken ct = default)
        => ToolDispatch.PreviewWithWorkspaceIdAsync(
            gate,
            workspaceId,
            async c =>
            {
                var canonicalWritePath = await ClientRootPathValidator
                    .ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                EditTools.EnsurePinnedTargetMatchesResolvedDocument(filePath, canonicalWritePath);
                return await suppressionService.AddPragmaWarningDisableAsync(
                    workspaceId, filePath, line, diagnosticId, canonicalWritePath, c).ConfigureAwait(false);
            },
            ct);

    /// <remarks>
    /// The classic cosmetic-pragma shape is a pair that wraps line 68 while the diagnostic
    /// actually fires at line 78: the suppression looks present in review but suppresses nothing.
    /// This tool performs no edits; widen a mis-scoped pair with <c>pragma_scope_widen</c>.
    /// </remarks>
    [McpServerTool(Name = "verify_pragma_suppresses", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("validation", "stable", true, false,
        "Verify an existing #pragma warning disable/restore pair covers a fire line."),
     Description("Check whether a '#pragma warning disable/restore' pair for a diagnostic id actually covers the given 1-based line. Detects 'cosmetic pragma' bugs where the pair wraps the wrong span. Read-only.")]
    public static Task<string> VerifyPragmaSuppresses(
        IWorkspaceExecutionGate gate,
        ISuppressionService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number that should be covered (the diagnostic fire site)")] int line,
        [Description("Diagnostic id whose suppression to check (e.g. CA2025)")] string diagnosticId,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => suppressionService.VerifyPragmaSuppressesAsync(
                workspaceId, filePath, line, diagnosticId, c),
            ct);

    /// <remarks>
    /// Both refusal conditions exist because relocating the restore across a
    /// <c>#region</c>/<c>#endregion</c> boundary, or into another
    /// <c>#pragma warning disable</c> for the same id, would silently change the effective scope
    /// of other suppressions. When the existing pair already covers the target line the call is an
    /// idempotent no-op.
    /// </remarks>
    [McpServerTool(Name = "pragma_scope_widen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("editing", "stable", false, false,
        "Extend an existing #pragma warning restore past a target line."),
     Description("Extend a matching '#pragma warning restore &lt;id&gt;' to cover an uncovered fire site. Refuses when the move would cross a #region/#endregion boundary or nest into another disable for the same id.")]
    public static Task<string> PragmaScopeWiden(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IPinnedSuppressionWriteService suppressionService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number that must be covered after the widen (the diagnostic fire site)")] int line,
        [Description("Diagnostic id whose 'restore' is being moved (e.g. CA2025)")] string diagnosticId,
        CancellationToken ct = default)
        => ToolDispatch.PreviewWithWorkspaceIdAsync(
            gate,
            workspaceId,
            async c =>
            {
                var canonicalWritePath = await ClientRootPathValidator
                    .ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                EditTools.EnsurePinnedTargetMatchesResolvedDocument(filePath, canonicalWritePath);
                return await suppressionService.WidenPragmaScopeAsync(
                    workspaceId, filePath, line, diagnosticId, canonicalWritePath, c).ConfigureAwait(false);
            },
            ct);
}
