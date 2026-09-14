using System.ComponentModel;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// MCP tool entry points for Roslyn refactorings (rename / organize-usings / format /
/// code-fix). WS1 phase 1.4 — each shim body delegates to the corresponding
/// <see cref="ToolDispatch"/> helper instead of carrying the 7-line dispatch
/// boilerplate inline. See <c>CodeActionTools</c> (canary, PR #305),
/// <c>BulkRefactoringTools</c> (phase 1.3), and
/// <c>ai_docs/plans/20260421T123658Z_post-audit-followups.md</c> for the migration
/// rationale and the deferred-generator blocker.
/// </summary>
[McpServerToolType]
public static class RefactoringTools
{
    /// <remarks>
    /// Prefer a <c>symbolHandle</c> from <c>enclosing_symbol</c> or <c>document_symbols</c> for
    /// precise targeting. For high-fan-out symbols, <c>summary=true</c> replaces per-file unified
    /// diffs with one-line summaries while the apply operation still rewrites every reference.
    /// </remarks>
    [McpServerTool(Name = "rename_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview a workspace-wide symbol rename and its affected edits. Prefer symbolHandle for precise targeting; set summary=true for high-fan-out symbols (>150 references) to fit the MCP output cap.")]
    [McpToolMetadata("refactoring", "stable", true, false,
        "Preview a rename refactoring.")]
    public static Task<string> PreviewRename(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("The new name for the symbol")] string newName,
        [Description("Optional: absolute path to the source file containing the symbol")] string? filePath = null,
        [Description("Optional: 1-based line number of the symbol to rename")] int? line = null,
        [Description("Optional: 1-based column number of the symbol to rename")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        [Description("Optional: fully qualified metadata name, e.g. Namespace.TypeName")] string? metadataName = null,
        [Description("When true, replace per-file unified diffs with compact one-line summaries. The stored preview still carries every real edit, so rename_apply rewrites all references correctly. Use for high-fan-out symbols (>150 refs) where the full diff exceeds the MCP output cap.")] bool summary = false,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => refactoringService.PreviewRenameAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName), newName, summary, c),
            ct);

    [McpServerTool(Name = "rename_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed rename refactoring using its preview token. Rejects stale tokens if the workspace has changed.")]
    [McpToolMetadata("refactoring", "stable", false, true,
        "Apply a previously previewed rename refactoring.")]
    public static Task<string> ApplyRename(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("Preview token from rename_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "rename_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.SymbolRename,
            invokedRoute: "rename_apply");

    [McpServerTool(Name = "organize_usings_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview organizing using directives in a file: removes unused usings and sorts them")]
    [McpToolMetadata("refactoring", "stable", true, false,
        "Preview using-directive cleanup.")]
    public static Task<string> PreviewOrganizeUsings(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Absolute path to the source file.")] string filePath,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => refactoringService.PreviewOrganizeUsingsAsync(workspaceId, filePath, c),
            ct);

    [McpServerTool(Name = "organize_usings_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed organize usings operation using its preview token")]
    [McpToolMetadata("refactoring", "stable", false, true,
        "Apply a previously previewed organize-usings operation.")]
    public static Task<string> ApplyOrganizeUsings(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("Preview token from organize_usings_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "organize_usings_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.OrganizeUsings,
            invokedRoute: "organize_usings_apply");

    [McpServerTool(Name = "format_document_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview formatting a document: applies standard C# formatting rules")]
    [McpToolMetadata("refactoring", "stable", true, false,
        "Preview document formatting.")]
    public static Task<string> PreviewFormatDocument(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Absolute path to the source file.")] string filePath,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => refactoringService.PreviewFormatDocumentAsync(workspaceId, filePath, c),
            ct);

    [McpServerTool(Name = "format_document_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed format document operation using its preview token")]
    [McpToolMetadata("refactoring", "stable", false, true,
        "Apply a previously previewed document format operation.")]
    public static Task<string> ApplyFormatDocument(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("Preview token from format_document_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "format_document_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.FormatDocument,
            invokedRoute: "format_document_apply");

    [McpServerTool(Name = "code_fix_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview a curated code fix for a specific diagnostic occurrence")]
    [McpToolMetadata("refactoring", "stable", true, false,
        "Preview a curated diagnostic code fix.")]
    public static Task<string> PreviewCodeFix(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file.")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        [Description("Optional: curated fix identifier from diagnostic_details")] string? fixId = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => refactoringService.PreviewCodeFixAsync(workspaceId, diagnosticId, filePath, line, column, fixId, c),
            ct);

    [McpServerTool(Name = "code_fix_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code fix using its preview token")]
    [McpToolMetadata("refactoring", "stable", false, true,
        "Apply a previously previewed curated code fix.")]
    public static Task<string> ApplyCodeFix(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("Preview token from code_fix_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "code_fix_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.CodeFix,
            invokedRoute: "code_fix_apply");

    [McpServerTool(Name = "format_range_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "stable", true, false,
        "Preview formatting a specific range within a document."),
     Description("Preview formatting a specific range within a document — more efficient than full-document formatting and produces smaller diffs")]
    public static Task<string> PreviewFormatRange(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Absolute path to the source file.")] string filePath,
        [Description("1-based start line of the range to format")] int startLine,
        [Description("1-based start column of the range to format")] int startColumn,
        [Description("1-based end line of the range to format")] int endLine,
        [Description("1-based end column of the range to format")] int endColumn,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => refactoringService.PreviewFormatRangeAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, c),
            ct);

    [McpServerTool(Name = "format_range_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previously previewed range format operation."),
     Description("Apply a previously previewed range format operation using its preview token")]
    public static Task<string> ApplyFormatRange(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("Preview token from format_range_preview.")] string previewToken,
        CancellationToken ct = default)
        => ToolDispatch.ApplyByTokenAsync(
            gate,
            previewStore,
            previewToken,
            c => refactoringService.ApplyRefactoringAsync(previewToken, "format_range_apply", c),
            ct,
            // preview-token-apply-route-provenance: bind this route to its producer family so a
            // token minted by a different *_preview is refused before any workspace mutation.
            expectedKind: PreviewKind.FormatRange,
            invokedRoute: "format_range_apply");

    /// <remarks>
    /// This is the in-memory analogue of <c>dotnet format --verify-no-changes</c>. Its response
    /// reports checked documents, violation count, per-file change counts, and elapsed time.
    /// </remarks>
    [McpServerTool(Name = "format_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Report documents that would change under Roslyn's formatter without applying edits (workspace-wide format-verify)."),
     Description("Report documents that Roslyn would reformat without applying edits. Use projectName to scope the workspace-wide check; results include violations and change counts.")]
    public static Task<string> FormatCheck(
        IWorkspaceExecutionGate gate,
        IFormatVerifyService formatVerifyService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Optional: restrict the check to a specific project name. Defaults to all projects.")] string? projectName = null,
        CancellationToken ct = default)
        => ToolDispatch.ReadByWorkspaceIdAsync(
            gate,
            workspaceId,
            c => formatVerifyService.CheckAsync(workspaceId, projectName, c),
            ct);
}
