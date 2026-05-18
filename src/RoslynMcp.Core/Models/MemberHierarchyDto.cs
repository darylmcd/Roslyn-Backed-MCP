namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the base, override, and sibling-implementation relationships for a member symbol.
/// </summary>
/// <param name="Symbol">The resolved member.</param>
/// <param name="BaseMembers">Members the resolved symbol overrides or implements (its base chain).</param>
/// <param name="Overrides">
/// True virtual/abstract overrides — members actually marked <c>override</c> of a virtual or
/// abstract declaration. Sibling interface implementations live in
/// <paramref name="SiblingInterfaceImplementations"/>, not here.
/// </param>
/// <param name="SiblingInterfaceImplementations">
/// Concrete implementations of an interface member across the solution (e.g., every
/// <c>IDisposable.Dispose</c> implementation). Empty when the resolved symbol is not an
/// interface member or has no concrete implementations.
/// </param>
public sealed record MemberHierarchyDto(
    SymbolDto Symbol,
    IReadOnlyList<SymbolDto> BaseMembers,
    IReadOnlyList<SymbolDto> Overrides,
    IReadOnlyList<SymbolDto> SiblingInterfaceImplementations);
