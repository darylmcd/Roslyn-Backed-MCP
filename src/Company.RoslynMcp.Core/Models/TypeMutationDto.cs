namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a member that mutates state on a target type.
/// </summary>
public sealed record MutatingMemberDto(
    string Name,
    string FullyQualifiedName,
    string Kind,
    string? FilePath,
    int? Line,
    IReadOnlyList<MutationCallerDto> ExternalCallers);

/// <summary>
/// Represents a caller that invokes a mutating member.
/// </summary>
public sealed record MutationCallerDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    string? ContainingMember,
    string? PreviewText,
    string CallerPhase);

/// <summary>
/// Represents state mutation analysis results for a type.
/// </summary>
public sealed record TypeMutationDto(
    SymbolDto Type,
    IReadOnlyList<MutatingMemberDto> MutatingMembers,
    string Summary);
