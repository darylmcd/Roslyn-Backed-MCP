namespace Company.RoslynMcp.Core.Models;

public sealed record MutatingMemberDto(
    string Name,
    string FullyQualifiedName,
    string Kind,
    string? FilePath,
    int? Line,
    IReadOnlyList<MutationCallerDto> ExternalCallers);

public sealed record MutationCallerDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    string? ContainingMember,
    string? PreviewText,
    string CallerPhase);

public sealed record TypeMutationDto(
    SymbolDto Type,
    IReadOnlyList<MutatingMemberDto> MutatingMembers,
    string Summary);
