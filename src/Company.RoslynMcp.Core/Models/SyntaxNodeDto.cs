namespace Company.RoslynMcp.Core.Models;

public sealed record SyntaxNodeDto(
    string Kind,
    string? Text,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    IReadOnlyList<SyntaxNodeDto>? Children);
