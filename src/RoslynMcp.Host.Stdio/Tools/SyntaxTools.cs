using System.ComponentModel;
using System.Text.Json;
using RoslynMcp.Core.Services;
using ModelContextProtocol.Server;
using McpServer = ModelContextProtocol.Server.McpServer;
using RoslynMcp.Host.Stdio.Catalog;

namespace RoslynMcp.Host.Stdio.Tools;

[McpServerToolType]
public static class SyntaxTools
{

    [McpServerTool(Name = "get_syntax_tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     McpToolMetadata("syntax", "stable", true, false,
        "Return a structured syntax tree for a document or range."),
     Description("Get the syntax tree (AST) for a source file or a specific line range. Returns a hierarchical tree of syntax nodes with their kinds and positions. THREE INDEPENDENT BUDGETS apply: maxOutputChars (LEAF TEXT only — does not bound structural JSON), maxNodes (total node count), maxTotalBytes (estimated total response size). The walker stops at the first cap hit and emits a TruncationNotice. Use maxTotalBytes to enforce the MCP cap regardless of leaf-text vs structural distribution — addresses the §3 stress test repro where maxOutputChars=20000 produced 229 KB on EncodingHelper.cs because structural JSON dominated.")]
    public static Task<string> GetSyntaxTree(
        McpServer server,
        IWorkspaceExecutionGate gate,
        ISyntaxService syntaxService,
        [Description("The workspace session identifier returned by workspace_load")] string workspaceId,
        [Description("Absolute path to the source file")] string filePath,
        [Description("Optional: start line (1-based) to limit the tree to a specific range")] int? startLine = null,
        [Description("Optional: end line (1-based) to limit the tree to a specific range")] int? endLine = null,
        [Description("Maximum depth of the syntax tree to return (default: 3)")] int maxDepth = 3,
        [Description("LEAF-TEXT-ONLY budget: caps the total characters emitted in node.Text across all leaves. Does NOT bound structural JSON. Use maxTotalBytes for total response size. Default 65536.")] int maxOutputChars = 65536,
        [Description("Caps the total number of syntax nodes emitted. Walker stops at the first node that would exceed and emits TruncationNotice. Default 5000.")] int maxNodes = 5000,
        [Description("Hard cap on estimated total JSON-serialized response size in bytes (~120 bytes per node + leaf text length). Use this to guarantee the response fits under the MCP cap. Default 65536.")] int maxTotalBytes = 65536,
        CancellationToken ct = default)
    {
        return ToolErrorHandler.ExecuteAsync("get_syntax_tree", () =>
            gate.RunReadAsync(workspaceId, async c =>
            {
                await ClientRootPathValidator.ValidatePathAgainstRootsAsync(server, filePath, c).ConfigureAwait(false);
                var result = await syntaxService.GetSyntaxTreeAsync(
                    workspaceId, filePath, startLine, endLine, maxDepth, c, maxOutputChars, maxNodes, maxTotalBytes);
                if (result is null) throw new KeyNotFoundException($"Document not found: {filePath}");
                return JsonSerializer.Serialize(result, JsonDefaults.Indented);
            }, ct));
    }
}
