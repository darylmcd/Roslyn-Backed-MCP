namespace Company.RoslynMcp.Core.Models;

public sealed record TypeHierarchyDto(
    string TypeName,
    string FullyQualifiedName,
    string? FilePath,
    int? StartLine,
    IReadOnlyList<TypeHierarchyDto>? BaseTypes,
    IReadOnlyList<TypeHierarchyDto>? DerivedTypes,
    IReadOnlyList<TypeHierarchyDto>? Interfaces);
