namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents the base and override relationships for a member symbol.
/// </summary>
public sealed record MemberHierarchyDto(
    SymbolDto Symbol,
    IReadOnlyList<SymbolDto> BaseMembers,
    IReadOnlyList<SymbolDto> Overrides);
