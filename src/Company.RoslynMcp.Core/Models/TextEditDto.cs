namespace Company.RoslynMcp.Core.Models;

public sealed record TextEditDto(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string NewText);

public sealed record TextEditResultDto(
    bool Success,
    string FilePath,
    int EditsApplied,
    IReadOnlyList<FileChangeDto> Changes);
