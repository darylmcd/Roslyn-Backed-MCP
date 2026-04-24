namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents definitions, references, and hierarchy relationships for a symbol.
/// </summary>
public sealed record SymbolRelationshipsDto(
    SymbolDto Symbol,
    IReadOnlyList<LocationDto> Definitions,
    IReadOnlyList<LocationDto> References,
    IReadOnlyList<LocationDto> Implementations,
    IReadOnlyList<SymbolDto> BaseMembers,
    IReadOnlyList<SymbolDto> Overrides);
