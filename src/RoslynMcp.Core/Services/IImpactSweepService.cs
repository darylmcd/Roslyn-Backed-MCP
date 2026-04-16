using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 9: after a symbol change (enum add, rename), run a structured sweep for downstream
/// impact — references, non-exhaustive switches, and mapper-shaped callsites.
/// </summary>
public interface IImpactSweepService
{
    /// <summary>
    /// Aggregates references + switch-exhaustiveness diagnostics + mapper callsites + DTO
    /// persistence findings for the symbol resolved at <paramref name="locator"/>.
    /// </summary>
    /// <param name="summary">
    /// (symbol-impact-sweep-output-size-blowup) When <c>true</c>, drops <see cref="LocationDto.PreviewText"/>
    /// from every <see cref="LocationDto"/> in the response (References + MapperCallsites). The
    /// truncated locations still carry file path + line + column + classification. Required on
    /// high-fan-out symbols where the default response exceeds the MCP cap (Jellyfin's
    /// 1452-ref BaseItem: ~886 KB). Default <c>false</c> preserves the v1.18 response shape.
    /// </param>
    /// <param name="maxItemsPerCategory">
    /// (symbol-impact-sweep-output-size-blowup) When non-null, caps the count of items in each
    /// list independently (References, MapperCallsites, SwitchExhaustivenessIssues). Items
    /// beyond the cap are dropped. Use this together with <paramref name="summary"/> to bound
    /// both per-item size AND total list length on extremely high-fan-out symbols.
    /// </param>
    Task<SymbolImpactSweepDto> SweepAsync(
        string workspaceId,
        SymbolLocator locator,
        CancellationToken ct,
        bool summary = false,
        int? maxItemsPerCategory = null);
}

/// <summary>
/// Aggregated output of <see cref="IImpactSweepService.SweepAsync"/>.
/// </summary>
/// <param name="PersistenceLayerFindings">
/// Item 10 (v1.18, <c>mapper-snapshot-dto-symmetry-check</c>): when the swept symbol is a
/// property carrying a JSON / DataMember attribute, finds paired DTO records (same property
/// name, sibling type) and the bidirectional mapper methods (<c>To*</c> / <c>From*</c>) that
/// should round-trip the property. Each entry flags the directions where the property is
/// present vs missing so reviewers spot asymmetric mappings.
/// </param>
public sealed record SymbolImpactSweepDto(
    string SymbolDisplay,
    IReadOnlyList<LocationDto> References,
    IReadOnlyList<DiagnosticDto> SwitchExhaustivenessIssues,
    IReadOnlyList<LocationDto> MapperCallsites,
    IReadOnlyList<string> SuggestedTasks,
    IReadOnlyList<PersistenceLayerFindingDto> PersistenceLayerFindings);

/// <summary>One persistence-layer finding for a property that round-trips through DTO mappers.</summary>
public sealed record PersistenceLayerFindingDto(
    string PropertyName,
    string DomainTypeDisplay,
    string DtoTypeDisplay,
    IReadOnlyList<string> DirectionsPresent,
    IReadOnlyList<string> DirectionsMissing,
    IReadOnlyList<string> Notes);
