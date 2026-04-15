using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Roslyn.Contracts;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Item 6 + Item 7: structural search/replace and string-literal-to-constant extraction tools.
/// Both live here because they share the preview/apply pattern and scope filters.
/// </summary>
[McpServerToolType]
public static class RestructureTools
{
    [McpServerTool(Name = "restructure_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview a syntax-tree pattern-based find-and-replace using __placeholder__ captures."),
     Description("Preview a syntax-tree pattern-based find-and-replace. Pattern and goal are C# expression or statement fragments; placeholders of the form __name__ capture and splice arbitrary sub-expressions. Returns a preview token redeemable via preview_multi_file_edit_apply.")]
    public static Task<string> PreviewRestructure(
        IWorkspaceExecutionGate gate,
        IRestructureService restructureService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("The pattern to match. C# expression or statement, with __name__ placeholders (e.g., 'return __items__;')")] string pattern,
        [Description("The replacement goal. Must be the same syntactic kind as pattern. Placeholders are substituted (e.g., 'return new { count = __items__.Length, items = __items__ };')")] string goal,
        [Description("Optional: restrict scope to this file path")] string? filePath = null,
        [Description("Optional: restrict scope to this project (name or file path)")] string? projectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("restructure_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await restructureService.PreviewRestructureAsync(
                    workspaceId, pattern, goal,
                    new RestructureScope(filePath, projectName), c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }

    [McpServerTool(Name = "replace_string_literals_preview", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false),
     McpToolMetadata("refactoring", "experimental", true, false,
        "Preview replacing string literals in argument/initializer position with a constant expression."),
     Description("Preview replacing string literals in argument or initializer position with a constant/identifier expression. Useful for magic-string centralization. Returns a preview token redeemable via preview_multi_file_edit_apply.")]
    public static Task<string> PreviewReplaceStringLiterals(
        IWorkspaceExecutionGate gate,
        IStringLiteralReplaceService stringLiteralReplaceService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Replacement pairs. Each has literalValue (the exact string literal to find, without quotes), replacementExpression (the C# expression text to substitute, e.g. 'Constants.MyValue'), and optional usingNamespace (namespace to add as using directive).")] StringLiteralReplacementDto[] replacements,
        [Description("Optional: restrict scope to this file path")] string? filePath = null,
        [Description("Optional: restrict scope to this project (name or file path)")] string? projectName = null,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("replace_string_literals_preview", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await stringLiteralReplaceService.PreviewReplaceAsync(
                    workspaceId, replacements, new RestructureScope(filePath, projectName), c).ConfigureAwait(false);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
