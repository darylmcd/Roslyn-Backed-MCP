namespace Company.RoslynMcp.Core.Models;

public sealed record MultiFileEditResultDto(
    bool Success,
    int FilesModified,
    IReadOnlyList<FileEditSummaryDto> Files);

public sealed record FileEditSummaryDto(
    string FilePath,
    int EditsApplied,
    string? Diff);
