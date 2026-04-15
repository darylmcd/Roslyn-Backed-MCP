using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class OperationTools
{
    [McpServerTool(Name = "get_operations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Get the IOperation tree for behavioral analysis at a source position."),
     Description("Get the IOperation tree (language-agnostic behavioral representation) for a syntax node. Returns operation kinds like Invocation, Assignment, Loop, etc. Use for behavioral pattern matching at a higher abstraction than syntax trees. IMPORTANT (UX-003): the column must point at the syntax token whose operation you want — calling on a token returns that token's operation, NOT the enclosing expression. To inspect a method call, place the column on the method-name identifier, not on '(' or whitespace; to inspect a binary expression, place it on the operator. If you only have an approximate cursor, walk outward by re-querying with adjacent columns until the desired operation kind appears.")]
    public static Task<string> GetOperations(
        IWorkspaceExecutionGate gate,
        IOperationService operationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number — must point at the token whose operation you want (UX-003)")] int column,
        [Description("Maximum depth of the operation tree to return (default: 3)")] int maxDepth = 3,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("get_operations", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await operationService.GetOperationsAsync(workspaceId, filePath, line, column, maxDepth, c);
                if (result is null)
                    return JsonSerializer.Serialize(new { message = "No IOperation found at the specified position." }, JsonDefaults.Indented);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
