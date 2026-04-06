using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class OperationTools
{
    [McpServerTool(Name = "get_operations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the IOperation tree (language-agnostic behavioral representation) for a syntax node. Returns operation kinds like Invocation, Assignment, Loop, etc. Use for behavioral pattern matching at a higher abstraction than syntax trees.")]
    public static Task<string> GetOperations(
        IWorkspaceExecutionGate gate,
        IOperationService operationService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number")] int column,
        [Description("Maximum depth of the operation tree to return (default: 3)")] int maxDepth = 3,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                var result = await operationService.GetOperationsAsync(workspaceId, filePath, line, column, maxDepth, c);
                if (result is null)
                    return JsonSerializer.Serialize(new { message = "No IOperation found at the specified position." }, JsonDefaults.Indented);
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
