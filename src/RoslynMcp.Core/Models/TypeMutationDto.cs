namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a member that mutates state on a target type. <see cref="MutationScopes"/>
/// classifies every kind of mutation detected on the member: <c>FieldWrite</c> (settable
/// property or instance-field reassignment), <c>CollectionWrite</c> (Add/Remove/Clear-style
/// call), <c>IO</c> (System.IO.File / Directory / FileStream / StreamWriter),
/// <c>Network</c> (HttpClient / TcpClient / UdpClient), <c>Process</c>
/// (System.Diagnostics.Process), or <c>Database</c> (DbCommand.Execute*). A single member
/// that performs compound mutations (e.g. both <c>IO</c> and <c>CollectionWrite</c>)
/// reports every applicable scope rather than only the highest severity. The list is
/// always non-empty for mutating members.
/// </summary>
public sealed record MutatingMemberDto(
    string Name,
    string FullyQualifiedName,
    string Kind,
    string? FilePath,
    int? Line,
    IReadOnlyList<MutationCallerDto> ExternalCallers,
    IReadOnlyList<string> MutationScopes);

/// <summary>
/// Represents a caller that invokes a mutating member.
/// </summary>
public sealed record MutationCallerDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    string? ContainingMember,
    string? PreviewText,
    string CallerPhase,
    LocationDto? Location = null);

/// <summary>
/// Represents state mutation analysis results for a type.
/// </summary>
public sealed record TypeMutationDto(
    SymbolDto Type,
    IReadOnlyList<MutatingMemberDto> MutatingMembers,
    string Summary);
