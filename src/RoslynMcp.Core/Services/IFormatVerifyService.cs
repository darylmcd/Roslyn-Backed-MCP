namespace RoslynMcp.Core.Services;

/// <summary>
/// Scans a workspace for documents that would change under Roslyn's formatter without
/// applying any mutations. Analogous to <c>dotnet format --verify-no-changes</c> but
/// entirely in-memory via <c>Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync</c>.
/// </summary>
public interface IFormatVerifyService
{
    /// <summary>
    /// Runs format verification for all documents in the workspace, or scoped to a single
    /// project when <paramref name="projectName"/> is supplied.
    /// </summary>
    Task<FormatCheckResultDto> CheckAsync(
        string workspaceId, string? projectName, CancellationToken ct);
}

/// <summary>
/// Result of a workspace-wide format verification pass.
/// </summary>
/// <param name="CheckedDocuments">Total documents the pass examined.</param>
/// <param name="ViolationCount">Count of documents whose content would change under <c>Formatter.FormatAsync</c>.</param>
/// <param name="Violations">Per-document summary of the files that would change.</param>
/// <param name="ElapsedMs">Wall-clock duration of the pass, in milliseconds.</param>
public sealed record FormatCheckResultDto(
    int CheckedDocuments,
    int ViolationCount,
    IReadOnlyList<FormatViolationDto> Violations,
    long ElapsedMs);

/// <summary>
/// A single document that would change under the formatter.
/// </summary>
/// <param name="FilePath">Absolute path to the offending source file.</param>
/// <param name="ChangeCount">Number of distinct <c>TextChange</c> spans Roslyn would apply.</param>
public sealed record FormatViolationDto(string FilePath, int ChangeCount);
