using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds all references, implementations, overrides, and base members for a symbol across
/// a solution, with classified usage locations and bulk lookup support.
/// </summary>
public interface IReferenceService
{
    /// <summary>
    /// Finds every reference to the symbol resolved at <paramref name="locator"/>.
    /// </summary>
    /// <param name="summary">
    /// When true (find-references-preview-text-inflates-response): suppresses per-ref
    /// preview text so the response stays small for high-fan-out symbols. Each returned
    /// <see cref="LocationDto"/> has <c>PreviewText = null</c>; file path + line + column
    /// + classification still populated. Default <c>false</c> preserves the v1.18.2 shape.
    /// </param>
    Task<IReadOnlyList<LocationDto>> FindReferencesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct, bool summary = false);
    Task<IReadOnlyList<LocationDto>> FindImplementationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> FindOverridesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<LocationDto>> FindBaseMembersAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<BulkReferenceResultDto>> FindReferencesBulkAsync(
        string workspaceId, IReadOnlyList<BulkSymbolLocator> symbols, bool includeDefinition, CancellationToken ct);
}
