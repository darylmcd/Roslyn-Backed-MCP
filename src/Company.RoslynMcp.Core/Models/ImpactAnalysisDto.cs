namespace Company.RoslynMcp.Core.Models;

public sealed record ImpactAnalysisDto(
    SymbolDto TargetSymbol,
    IReadOnlyList<LocationDto> DirectReferences,
    IReadOnlyList<SymbolDto> AffectedDeclarations,
    IReadOnlyList<string> AffectedProjects,
    string Summary);
