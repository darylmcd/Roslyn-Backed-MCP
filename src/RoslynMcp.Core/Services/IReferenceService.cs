using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds all references, implementations, overrides, and base members for a symbol across
/// a solution, with classified usage locations and bulk lookup support.
/// </summary>
public interface IReferenceService
{
    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct);
}
