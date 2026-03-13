namespace Company.RoslynMcp.Core.Models;

public sealed record LocationDto(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string? ContainingMember,
    string? PreviewText);
