using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Provides preview and apply operations for common source-level refactorings such as rename,
/// organize usings, document formatting, and diagnostic code fixes.
/// </summary>
public interface IRefactoringService
{
    /// <summary>
    /// Previews renaming the symbol identified by <paramref name="locator"/> across the workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="locator">Identifies the symbol to rename.</param>
    /// <param name="newName">The replacement name to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewRenameAsync(string workspaceId, SymbolLocator locator, string newName, CancellationToken ct);

    /// <summary>
    /// Applies a previously previewed refactoring to the workspace.
    /// </summary>
    /// <param name="previewToken">The token returned by a prior preview call.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, CancellationToken ct);

    /// <summary>
    /// Item #4 — apply a preview with an explicit <paramref name="force"/> override.
    /// When the stored preview's diff was truncated and <paramref name="force"/> is
    /// <see langword="false"/> (the default for every *_apply tool), the apply is
    /// refused with an actionable error message. Pass <see langword="true"/> to
    /// apply without full visibility — the agent accepts responsibility for the
    /// unreviewed changes.
    /// </summary>
    Task<ApplyResultDto> ApplyRefactoringAsync(string previewToken, bool force, CancellationToken ct);

    /// <summary>
    /// Previews removing unused <c>using</c> directives and sorting the remaining ones for the given file.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewOrganizeUsingsAsync(string workspaceId, string filePath, CancellationToken ct);

    /// <summary>
    /// Previews applying the default Roslyn formatting rules to the given file.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewFormatDocumentAsync(string workspaceId, string filePath, CancellationToken ct);

    /// <summary>
    /// Previews applying the default Roslyn formatting rules to a specific range within a file.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <param name="startLine">The 1-based start line of the range to format.</param>
    /// <param name="startColumn">The 1-based start column of the range to format.</param>
    /// <param name="endLine">The 1-based end line of the range to format.</param>
    /// <param name="endColumn">The 1-based end column of the range to format.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewFormatRangeAsync(
        string workspaceId, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken ct);

    /// <summary>
    /// Previews applying a curated code fix for the specified diagnostic.
    /// </summary>
    /// <param name="workspaceId">The workspace session identifier.</param>
    /// <param name="diagnosticId">The diagnostic identifier to fix (e.g., <c>CS8600</c>).</param>
    /// <param name="filePath">The absolute path to the file where the diagnostic occurs.</param>
    /// <param name="line">The 1-based line number of the diagnostic.</param>
    /// <param name="column">The 1-based column number of the diagnostic.</param>
    /// <param name="fixId">The specific fix identifier to apply, or <see langword="null"/> to apply the first available fix.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RefactoringPreviewDto> PreviewCodeFixAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        string? fixId,
        CancellationToken ct);
}
