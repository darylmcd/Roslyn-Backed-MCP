namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents the result of applying text edits across multiple files.
/// </summary>
/// <param name="Verification">
/// When <c>verify=true</c> was requested, carries a single batch-level post-edit
/// compile-check outcome. The verify pass runs ONCE at the end of the batch
/// across the union of owning projects, not once per file — that matches the
/// single-snapshot undo semantics of <c>apply_multi_file_edit</c>. <c>null</c>
/// on the default (<c>verify=false</c>) path.
/// </param>
public sealed record MultiFileEditResultDto(
    bool Success,
    int FilesModified,
    IReadOnlyList<FileEditSummaryDto> Files,
    VerifyOutcomeDto? Verification = null);

/// <summary>
/// Represents the edit outcome for a single file in a multi-file edit operation.
/// </summary>
public sealed record FileEditSummaryDto(
    string FilePath,
    int EditsApplied,
    string? Diff);
