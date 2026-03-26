using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

public interface ISymbolNavigationService
{
    Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct);
}
