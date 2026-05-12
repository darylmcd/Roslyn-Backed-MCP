using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Searches for symbols by name pattern across a loaded workspace, retrieves detailed symbol
/// information, and lists document-level symbol declarations.
/// </summary>
public interface ISymbolSearchService
{
    /// <param name="maxResults">
    /// symbol-search-pagination: hard internal cap on the number of matching symbols collected
    /// before offset/limit slicing is applied by the caller. Defaults to 1000. Callers that
    /// need accurate <c>totalCount</c> must pass a value large enough to accommodate all
    /// matching symbols; the tool layer uses <c>offset + limit</c> floored at 1000 to avoid
    /// over-fetching on targeted queries.
    /// </param>
    Task<IReadOnlyList<SymbolDto>> SearchSymbolsAsync(
        string workspaceId, string query, string? projectFilter, string? kindFilter, string? namespaceFilter, int maxResults, CancellationToken ct);
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
    /// <summary>
    /// document-symbols-accepts-symbol-handle: overload that resolves a <see cref="SymbolLocator"/>
    /// (handle, metadata name, or file+position) to its source file path, then delegates to the
    /// <c>filePath</c>-based overload. Throws <see cref="KeyNotFoundException"/> when the locator
    /// cannot be resolved or the resolved symbol has no source location.
    /// </summary>
    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
