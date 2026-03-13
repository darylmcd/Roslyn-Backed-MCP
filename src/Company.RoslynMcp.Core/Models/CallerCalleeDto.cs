namespace Company.RoslynMcp.Core.Models;

public sealed record CallerCalleeDto(
    SymbolDto Symbol,
    IReadOnlyList<LocationDto> Callers,
    IReadOnlyList<LocationDto> Callees);
