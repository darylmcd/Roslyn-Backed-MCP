namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a compiler, analyzer, or workspace diagnostic.
/// </summary>
public sealed record DiagnosticDto(
    string Id,
    string Message,
    string Severity,
    string Category,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] string? FilePath,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? StartLine,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? StartColumn,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? EndLine,
    [property: Obsolete(LocationDtoDeprecation.FlatMemberMessage)] int? EndColumn,
    LocationDto? Location = null);
