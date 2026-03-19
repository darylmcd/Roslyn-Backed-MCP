namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a span within a source file.
/// </summary>
public sealed record LocationDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText,
    string? Classification = null);
