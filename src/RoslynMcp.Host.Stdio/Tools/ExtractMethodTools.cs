using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class ExtractMethodTools
{
    [McpServerTool(Name = "extract_method_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpToolMetadata("refactoring", "stable", true, false,
        "Preview extracting selected statements into a new method. Uses data-flow analysis to infer parameters and return values.")]
    [Description("Preview extracting selected statements into a new method. Uses data-flow analysis to infer parameters and return values. Selection must cover complete statements in the same block without return statements.")]
    public static Task<string> PreviewExtractMethod(
        IWorkspaceExecutionGate gate,
        IExtractMethodService extractMethodService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based start line of the selection")] int startLine,
        [Description("1-based start column of the selection")] int startColumn,
        [Description("1-based end line of the selection")] int endLine,
        [Description("1-based end column of the selection")] int endColumn,
        [Description("Name for the extracted method")] string methodName,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await extractMethodService.PreviewExtractMethodAsync(
                workspaceId, filePath, startLine, startColumn, endLine, endColumn, methodName, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }

    [McpServerTool(Name = "extract_method_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [McpToolMetadata("refactoring", "experimental", false, true,
        "Apply a previously previewed extract method refactoring.")]
    [Description("Apply a previously previewed extract method refactoring.")]
    public static Task<string> ApplyExtractMethod(
        IWorkspaceExecutionGate gate,
        IRefactoringService refactoringService,
        IPreviewStore previewStore,
        [Description("The preview token returned by extract_method_preview")] string previewToken,
        CancellationToken ct = default)
    {
        var wsId = previewStore.PeekWorkspaceId(previewToken)
            ?? throw new KeyNotFoundException($"Preview token '{previewToken}' not found or expired.");
        return gate.RunWriteAsync(wsId, async c =>
        {
            var result = await refactoringService.ApplyRefactoringAsync(previewToken, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
