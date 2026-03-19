namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a symbol together with its caller and callee relationships.
/// </summary>
public sealed record CallerCalleeDto(
    SymbolDto Symbol,
    IReadOnlyList<LocationDto> Callers,
    IReadOnlyList<LocationDto> Callees);
