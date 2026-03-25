namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the direct and indirect impact of changing a target symbol.
/// </summary>
public sealed record ImpactAnalysisDto(
    SymbolDto TargetSymbol,
    IReadOnlyList<LocationDto> DirectReferences,
    IReadOnlyList<SymbolDto> AffectedDeclarations,
    IReadOnlyList<string> AffectedProjects,
    string Summary);
