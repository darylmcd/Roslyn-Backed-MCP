namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the base, derived, and implemented type relationships for a type.
/// </summary>
public sealed record TypeHierarchyDto(
    string TypeName,
    string FullyQualifiedName,
    string? FilePath,
    int? StartLine,
    IReadOnlyList<TypeHierarchyDto>? BaseTypes,
    IReadOnlyList<TypeHierarchyDto>? DerivedTypes,
    IReadOnlyList<TypeHierarchyDto>? Interfaces);
