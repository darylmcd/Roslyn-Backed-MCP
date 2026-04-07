using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Finds mutable members of a type, classifies property writes, analyzes type usages by role,
/// and performs impact analysis for a symbol.
/// </summary>
public interface IMutationAnalysisService
{
    Task<ImpactAnalysisDto?> AnalyzeImpactAsync(
        string workspaceId,
        SymbolLocator locator,
        ImpactAnalysisPaging paging,
        CancellationToken ct);
    Task<IReadOnlyList<PropertyWriteDto>> FindPropertyWritesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);

    /// <summary>
    /// FLAG-3C: Like <see cref="FindPropertyWritesAsync"/> but also returns the resolved
    /// symbol kind so callers can disambiguate "property has zero writes" from "position
    /// resolved to a non-property symbol (e.g., a field)".
    /// </summary>
    Task<(IReadOnlyList<PropertyWriteDto> Writes, string? ResolvedSymbolKind)> FindPropertyWritesWithMetadataAsync(
        string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<IReadOnlyList<TypeUsageDto>> FindTypeUsagesAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
    Task<TypeMutationDto?> FindTypeMutationsAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
}
