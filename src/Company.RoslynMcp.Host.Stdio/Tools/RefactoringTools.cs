using System.ComponentModel;
using System.Text.Json;
using Company.RoslynMcp.Core.Models;
using Company.RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class RefactoringTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "rename_preview"), Description("Preview a rename refactoring: shows all files and changes that would result from renaming a symbol")]
    public static async Task<string> PreviewRename(
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("The new name for the symbol")] string newName,
        [Description("Optional: absolute path to the source file containing the symbol")] string? filePath = null,
        [Description("Optional: 1-based line number of the symbol to rename")] int? line = null,
        [Description("Optional: 1-based column number of the symbol to rename")] int? column = null,
        [Description("Optional: stable symbol handle returned by other semantic tools")] string? symbolHandle = null,
        CancellationToken ct = default)
    {
        var result = await refactoringService.PreviewRenameAsync(
            workspaceId,
            CreateLocator(filePath, line, column, symbolHandle),
            newName,
            ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "rename_apply"), Description("Apply a previously previewed rename refactoring using its preview token. Rejects stale tokens if the workspace has changed.")]
    public static async Task<string> ApplyRename(
        IRefactoringService refactoringService,
        [Description("The preview token returned by rename_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var result = await refactoringService.ApplyRefactoringAsync(previewToken, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "organize_usings_preview"), Description("Preview organizing using directives in a file: removes unused usings and sorts them")]
    public static async Task<string> PreviewOrganizeUsings(
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var result = await refactoringService.PreviewOrganizeUsingsAsync(workspaceId, filePath, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "organize_usings_apply"), Description("Apply a previously previewed organize usings operation using its preview token")]
    public static async Task<string> ApplyOrganizeUsings(
        IRefactoringService refactoringService,
        [Description("The preview token returned by organize_usings_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var result = await refactoringService.ApplyRefactoringAsync(previewToken, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "format_document_preview"), Description("Preview formatting a document: applies standard C# formatting rules")]
    public static async Task<string> PreviewFormatDocument(
        IRefactoringService refactoringService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        CancellationToken ct = default)
    {
        var result = await refactoringService.PreviewFormatDocumentAsync(workspaceId, filePath, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "format_document_apply"), Description("Apply a previously previewed format document operation using its preview token")]
    public static async Task<string> ApplyFormatDocument(
        IRefactoringService refactoringService,
        [Description("The preview token returned by format_document_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var result = await refactoringService.ApplyRefactoringAsync(previewToken, ct);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static SymbolLocator CreateLocator(string? filePath, int? line, int? column, string? symbolHandle)
    {
        if (!string.IsNullOrWhiteSpace(symbolHandle))
        {
            return SymbolLocator.ByHandle(symbolHandle);
        }

        if (!string.IsNullOrWhiteSpace(filePath) && line.HasValue && column.HasValue)
        {
            return SymbolLocator.BySource(filePath, line.Value, column.Value);
        }

        throw new ArgumentException("Provide either filePath/line/column or symbolHandle.");
    }
}
