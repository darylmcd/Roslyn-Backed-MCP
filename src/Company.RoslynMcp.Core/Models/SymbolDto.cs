namespace Company.RoslynMcp.Core.Models;

public sealed record SymbolDto(
    string Name,
    string FullyQualifiedName,
    string? SymbolHandle,
    string Kind,
    string? ContainingType,
    string? Namespace,
    string? Project,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    string? ReturnType,
    IReadOnlyList<string>? Parameters,
    IReadOnlyList<string>? Modifiers,
    IReadOnlyList<string>? BaseTypes,
    IReadOnlyList<string>? Interfaces,
    string? Documentation,
    bool? HasGetter = null,
    bool? HasSetter = null,
    string? SetterAccessibility = null);
