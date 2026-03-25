namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of applying text edits across multiple files.
/// </summary>
public sealed record MultiFileEditResultDto(
    bool Success,
    int FilesModified,
    IReadOnlyList<FileEditSummaryDto> Files);

/// <summary>
/// Represents the edit outcome for a single file in a multi-file edit operation.
/// </summary>
public sealed record FileEditSummaryDto(
    string FilePath,
    int EditsApplied,
    string? Diff);
