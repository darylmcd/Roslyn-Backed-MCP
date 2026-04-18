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

    [McpServerTool(Name = "extract_shared_expression_to_helper_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpToolMetadata("refactoring", "experimental", true, false,
        "Preview extracting a shared sub-expression into a synthesized private static helper and rewriting every structurally-identical call site in the scope.")]
    [Description("Preview extracting a shared sub-expression at an example span into a synthesized static helper, then rewriting every structurally-identical call site in the example's containing type (or entire project when allowCrossFile=true). Refuses when the expression has fewer than 2 occurrences, when a candidate site's free-variable semantic types differ from the example, or when allowCrossFile=false and a hit resides in a different containing type. Complements extract_method_preview (statement-block, single-function) by handling the N-function shape.")]
    public static Task<string> PreviewExtractSharedExpressionToHelper(
        IWorkspaceExecutionGate gate,
        IExtractMethodService extractMethodService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to a source file containing one instance of the expression")] string exampleFilePath,
        [Description("1-based start line of the example expression")] int exampleStartLine,
        [Description("1-based start column of the example expression")] int exampleStartColumn,
        [Description("1-based end line of the example expression")] int exampleEndLine,
        [Description("1-based end column of the example expression")] int exampleEndColumn,
        [Description("Name for the synthesized helper method")] string helperName,
        [Description("Accessibility for the synthesized helper: 'private', 'internal', or 'public'. Defaults to 'private'.")] string helperAccessibility = "private",
        [Description("When true, scans the entire project for structurally-identical matches. When false (default), the scan is restricted to the example expression's containing type.")] bool allowCrossFile = false,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            var result = await extractMethodService.PreviewExtractSharedExpressionToHelperAsync(
                workspaceId, exampleFilePath,
                exampleStartLine, exampleStartColumn,
                exampleEndLine, exampleEndColumn,
                helperName, helperAccessibility, allowCrossFile, c);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
