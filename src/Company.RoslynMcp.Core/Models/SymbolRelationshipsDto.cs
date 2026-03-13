namespace Company.RoslynMcp.Core.Models;

public sealed record SymbolRelationshipsDto(
    SymbolDto Symbol,
    IReadOnlyList<LocationDto> Definitions,
    IReadOnlyList<LocationDto> References,
    IReadOnlyList<LocationDto> Implementations,
    IReadOnlyList<LocationDto> BaseMembers,
    IReadOnlyList<LocationDto> Overrides);
