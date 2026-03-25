using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class RefactoringTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewRenameAsync(workspaceId, SymbolLocatorFactory.Create(filePath, line, column, symbolHandle), newName, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "rename_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed rename refactoring using its preview token. Rejects stale tokens if the workspace has changed.")]
    public static Task<string> ApplyRename(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The preview token returned by rename_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.ApplyGateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
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
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewOrganizeUsingsAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "organize_usings_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed organize usings operation using its preview token")]
    public static Task<string> ApplyOrganizeUsings(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The preview token returned by organize_usings_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.ApplyGateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
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
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewFormatDocumentAsync(workspaceId, filePath, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "format_document_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed format document operation using its preview token")]
    public static Task<string> ApplyFormatDocument(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The preview token returned by format_document_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.ApplyGateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
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
            gate.RunAsync(workspaceId, async c =>
            {
                var result = await refactoringService.PreviewCodeFixAsync(workspaceId, diagnosticId, filePath, line, column, fixId, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }

    [McpServerTool(Name = "code_fix_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description("Apply a previously previewed code fix using its preview token")]
    public static Task<string> ApplyCodeFix(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        [Description("The preview token returned by code_fix_preview")] string previewToken,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(WorkspaceExecutionGate.ApplyGateKey, async c =>
            {
                var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
                return JsonSerializer.Serialize(result, JsonOptions);
            }, ct));
    }
}
