using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class OperationTools
{
    /// <remarks>
    /// The column must identify the token whose operation is wanted, not merely its enclosing
    /// expression. For calls, target the method-name identifier; for binary expressions, target
    /// the operator. When the cursor is approximate, query adjacent columns until the expected
    /// operation kind appears.
    /// </remarks>
    [McpServerTool(Name = "get_operations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("advanced-analysis", "stable", true, false,
        "Get the IOperation tree for behavioral analysis at a source position."),
     Description("Return the language-agnostic IOperation tree at an exact source token for behavioral analysis. Use a method identifier or operator token; approximate cursors may select a narrower operation.")]
    public static Task<string> GetOperations(
        McpServer server,
        IWorkspaceExecutionGate gate,
        IOperationService operationService,
        [Description("Workspace session id from workspace_load.")] string workspaceId,
        [Description("Absolute path to the source file.")] string filePath,
        [Description("1-based line number")] int line,
        [Description("1-based column number — must point at the token whose operation you want (UX-003)")] int column,
        [Description("Maximum depth of the operation tree to return (default: 3)")] int maxDepth = 3,
        CancellationToken ct = default)
    {
        return gate.RunReadAsync(workspaceId, async c =>
        {
            await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
            var result = await operationService.GetOperationsAsync(workspaceId, filePath, line, column, maxDepth, c);
            if (result is null)
                return JsonSerializer.Serialize(new { message = "No IOperation found at the specified position." }, JsonDefaults.Indented);
            return JsonSerializer.Serialize(result, JsonDefaults.Indented);
        }, ct);
    }
}
