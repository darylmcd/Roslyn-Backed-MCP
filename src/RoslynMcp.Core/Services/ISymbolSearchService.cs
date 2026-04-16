using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Searches for symbols by name pattern across a loaded workspace, retrieves detailed symbol
/// information, and lists document-level symbol declarations.
/// </summary>
public interface ISymbolSearchService
{
    Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int limit, CancellationToken ct);
    /// <param name="allowAdjacent">
    /// symbol-info-lenient-whitespace-resolution: when <see langword="false"/> (the default),
    /// a caret that falls on whitespace adjacent to an identifier does NOT resolve to that
    /// identifier — the caller gets a <see langword="null"/> result they can distinguish from
    /// a legitimate hit. Set <see langword="true"/> to opt into the pre-v1.19.1 lenient
    /// behavior where the resolver walks to the preceding token when the exact-position lookup
    /// misses.
    /// </param>
    Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool allowAdjacent = false);
    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct);
}
