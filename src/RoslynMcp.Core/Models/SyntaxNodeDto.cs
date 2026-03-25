namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a syntax node and its child nodes.
/// </summary>
public sealed record SyntaxNodeDto(
    string Kind,
    string? Text,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    IReadOnlyList<SyntaxNodeDto>? Children);
