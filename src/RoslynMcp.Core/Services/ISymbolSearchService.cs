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
    Task<SymbolDto?> GetSymbolInfoAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<DocumentSymbolDto>> GetDocumentSymbolsAsync(string workspaceId, string filePath, CancellationToken ct);
}
