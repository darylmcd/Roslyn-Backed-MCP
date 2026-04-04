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
    /// <param name="maxOutputChars">Approximate cap on leaf text accumulation; remaining tree is replaced with a truncation notice when exceeded.</param>
    /// <returns>The root syntax node, or <see langword="null"/> if the file is not found in the workspace.</returns>
    Task<SyntaxNodeDto?> GetSyntaxTreeAsync(
        string workspaceId,
        string filePath,
        int? startLine,
        int? endLine,
        int maxDepth,
        CancellationToken ct,
        int maxOutputChars = 65536);
}
