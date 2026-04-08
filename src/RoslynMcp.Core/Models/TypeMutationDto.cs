namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a member that mutates state on a target type. <see cref="MutationScope"/>
/// classifies the kind of mutation: <c>FieldWrite</c> (settable property or instance-field
/// reassignment), <c>CollectionWrite</c> (Add/Remove/Clear-style call), <c>IO</c>
/// (System.IO.File / Directory / FileStream / StreamWriter), <c>Network</c>
/// (HttpClient / TcpClient / UdpClient), <c>Process</c> (System.Diagnostics.Process),
/// or <c>Database</c> (DbCommand.Execute*). Defaults to <c>FieldWrite</c> for backward
/// compatibility with callers that did not previously inspect the field.
/// </summary>
public sealed record MutatingMemberDto(
    string Name,
    string FullyQualifiedName,
    string Kind,
    string? FilePath,
    int? Line,
    IReadOnlyList<MutationCallerDto> ExternalCallers,
    string MutationScope = "FieldWrite");

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
