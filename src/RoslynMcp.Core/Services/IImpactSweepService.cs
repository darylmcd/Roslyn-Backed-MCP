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
public sealed record SymbolImpactSweepDto(
    string SymbolDisplay,
    IReadOnlyList<LocationDto> References,
    IReadOnlyList<DiagnosticDto> SwitchExhaustivenessIssues,
    IReadOnlyList<LocationDto> MapperCallsites,
    IReadOnlyList<string> SuggestedTasks);
