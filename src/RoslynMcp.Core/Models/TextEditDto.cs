namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a text edit over a source span.
/// </summary>
public sealed record TextEditDto(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string NewText);

/// <summary>
/// Represents the result of applying one or more text edits to a file.
/// </summary>
public sealed record TextEditResultDto(
    bool Success,
    string FilePath,
    int EditsApplied,
    IReadOnlyList<FileChangeDto> Changes);
