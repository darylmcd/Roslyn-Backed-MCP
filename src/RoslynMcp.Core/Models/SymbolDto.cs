namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a code symbol and its metadata, source location, and signature details.
/// </summary>
public sealed record SymbolDto(
    string Name,
    string FullyQualifiedName,
    string? SymbolHandle,
    string Kind,
    string? ContainingType,
    string? Namespace,
    string? Project,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] string? FilePath,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? StartLine,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? StartColumn,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? EndLine,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? EndColumn,
    string? ReturnType,
    IReadOnlyList<string>? Parameters,
    IReadOnlyList<string>? Modifiers,
    IReadOnlyList<string>? BaseTypes,
    IReadOnlyList<string>? Interfaces,
    string? Documentation,
    bool? HasGetter = null,
    bool? HasSetter = null,
    string? SetterAccessibility = null,
    LocationDto? Location = null);
