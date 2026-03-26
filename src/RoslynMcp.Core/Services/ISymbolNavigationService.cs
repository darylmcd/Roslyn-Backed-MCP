using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Navigates to symbol definitions and type definitions, and resolves the enclosing symbol
/// at a given source position.
/// </summary>
public interface ISymbolNavigationService
{
    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct);
}
