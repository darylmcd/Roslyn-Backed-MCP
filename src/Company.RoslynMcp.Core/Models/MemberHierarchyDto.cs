namespace Company.RoslynMcp.Core.Models;

public sealed record MemberHierarchyDto(
    SymbolDto Symbol,
    IReadOnlyList<SymbolDto> BaseMembers,
    IReadOnlyList<SymbolDto> Overrides);
