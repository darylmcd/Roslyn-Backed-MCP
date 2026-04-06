using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class RefactoringTools
{

    [McpServerTool(Name = "rename_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview a rename refactoring: shows all files and changes that would result from renaming a symbol")]
    public static Task<string> PreviewRename(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("The new name for the symbol")] string newName,
        [Description("Optional: absolute path to the source file containing the symbol")] string? filePath = null,
        [Description("Optional: 1-based line number of the symbol to rename")] int? line = null,
        [Description("Optional: 1-based column number of the symbol to rename")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewRenameAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle, metadataName: null, supportsMetadataName: false), newName, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "rename_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed rename refactoring using its preview token. Rejects stale tokens if the workspace has changed.")]
    public static Task<string> ApplyRename(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by rename_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "organize_usings_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview organizing using directives in a file: removes unused usings and sorts them")]
    public static Task<string> PreviewOrganizeUsings(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewOrganizeUsingsAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "organize_usings_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed organize usings operation using its preview token")]
    public static Task<string> ApplyOrganizeUsings(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by organize_usings_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "format_document_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview formatting a document: applies standard C# formatting rules")]
    public static Task<string> PreviewFormatDocument(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewFormatDocumentAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "format_document_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed format document operation using its preview token")]
    public static Task<string> ApplyFormatDocument(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by format_document_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "code_fix_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false), Description("Preview a curated code fix for a specific diagnostic occurrence")]
    public static Task<string> PreviewCodeFix(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Diagnostic identifier, e.g. CS8019")] string diagnosticId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        [Description("Optional: curated fix identifier from diagnostic_details")] string? fixId = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewCodeFixAsync(workspaceId, diagnosticId, filePath, line, column, fixId, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "code_fix_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code fix using its preview token")]
    public static Task<string> ApplyCodeFix(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by code_fix_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

    [McpServerTool(Name = "format_range_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Preview formatting a specific range within a document — more efficient than full-document formatting and produces smaller diffs")]
    public static Task<string> PreviewFormatRange(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line of the range to format")] int startLine,
        [Description("1-based start column of the range to format")] int startColumn,
        [Description("1-based end line of the range to format")] int endLine,
        [Description("1-based end column of the range to format")] int endColumn,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewFormatRangeAsync(workspaceId, filePath, startLine, startColumn, endLine, endColumn, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "format_range_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
     Description("Apply a previously previewed range format operation using its preview token")]
    public static Task<string> ApplyFormatRange(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by format_range_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
        {
            var wsId = previewStore.PeekWorkspaceId(previewToken)
                ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
            return gate.RunWriteAsync(wsId, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct);
        });
    }

}
