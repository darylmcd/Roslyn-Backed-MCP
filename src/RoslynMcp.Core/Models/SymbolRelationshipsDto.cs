namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents definitions, references, and hierarchy relationships for a symbol.
/// </summary>
/// <param name="Hint">
/// Optional human-readable hint explaining why the response is short-circuited (e.g. the cursor
/// landed on a builtin-type token like <c>void</c> or <c>int</c> with <c>preferDeclaringMember=false</c>,
/// which would otherwise enumerate every reference to the special type solution-wide). Null when the
/// envelope is a normal, populated result.
/// </param>
public sealed record SymbolRelationshipsDto(
    SymbolDto Symbol,
    IReadOnlyList<LocationDto> Definitions,
    IReadOnlyList<LocationDto> References,
    IReadOnlyList<LocationDto> Implementations,
    IReadOnlyList<SymbolDto> BaseMembers,
    IReadOnlyList<SymbolDto> Overrides,
    string? Hint = null);
