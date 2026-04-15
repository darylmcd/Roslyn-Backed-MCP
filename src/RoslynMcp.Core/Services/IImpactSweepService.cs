using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Item 9: after a symbol change (enum add, rename), run a structured sweep for downstream
/// impact — references, non-exhaustive switches, and mapper-shaped callsites.
/// </summary>
public interface IImpactSweepService
{
    Task<SymbolImpactSweepDto> SweepAsync(string workspaceId, SymbolLocator locator, CancellationToken ct);
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
