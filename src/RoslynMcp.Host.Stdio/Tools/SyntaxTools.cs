using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SyntaxTools
{

    [McpServerTool(Name = "get_syntax_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the syntax tree (AST) for a source file or a specific line range. Returns a hierarchical tree of syntax nodes with their kinds and positions.")]
    public static Task<string> GetSyntaxTree(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISyntaxService syntaxService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("Optional: start line (1-based) to limit the tree to a specific range")] int? startLine = null,
        [Description("Optional: end line (1-based) to limit the tree to a specific range")] int? endLine = null,
        [Description("Maximum depth of the syntax tree to return (default: 3)")] int maxDepth = 3,
        [Description("Approximate maximum characters of leaf text to accumulate before truncating the tree (default: 65536).")] int maxOutputChars = 65536,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync(() =>
            gate.RunAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await syntaxService.GetSyntaxTreeAsync(
                    workspaceId, filePath, startLine, endLine, maxDepth, c, maxOutputChars);
                if (result is null) throw new KeyNotFoundException($"Document not found: {filePath}");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
