using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides access to the Roslyn syntax tree for documents in a loaded workspace.
/// </summary>
public interface ISyntaxService
{
    /// <summary>
    /// Returns the structured syntax tree for the given file, optionally restricted to a line range.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="startLine">The 1-based start line of the range to return, or <see langword="null"/> for the start of the file.</param>
    /// <param name="endLine">The 1-based end line of the range to return, or <see langword="null"/> for the end of the file.</param>
    /// <param name="maxDepth">The maximum recursion depth for child nodes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="maxOutputChars">
    /// LEAF-TEXT-only budget. Caps the total characters emitted in <c>SyntaxNodeDto.Text</c>
    /// across all leaf nodes. Does NOT bound structural JSON (kinds, positions, nesting) —
    /// for that, use <paramref name="maxTotalBytes"/>.
    /// </param>
    /// <param name="maxNodes">
    /// (get-syntax-tree-max-output-chars-incomplete-cap) Caps the total number of
    /// <see cref="SyntaxNodeDto"/> records emitted. The walker stops at the first node that
    /// would push the count past this limit and emits a TruncationNotice. Default 5000.
    /// </param>
    /// <param name="maxTotalBytes">
    /// (get-syntax-tree-max-output-chars-incomplete-cap) Hard cap on the estimated total
    /// JSON-serialized response size in bytes (~120 bytes structural overhead per node + leaf
    /// text length). Use this to enforce the MCP cap regardless of leaf-text vs structural
    /// distribution — addresses the §3 stress test repro where <c>maxOutputChars=20000</c>
    /// produced 229 KB on EncodingHelper.cs because structural JSON dominated. Default 65536.
    /// </param>
    /// <returns>The root syntax node, or <see langword="null"/> if the file is not found in the workspace.</returns>
    Task<SyntaxNodeDto?> GetSyntaxTreeAsync(
        string workspaceId,
        string filePath,
        int? startLine,
        int? endLine,
        int maxDepth,
        CancellationToken ct,
        int maxOutputChars = 65536,
        int maxNodes = 5000,
        int maxTotalBytes = 65536);
}
