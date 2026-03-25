namespace RoslynMcp.Core.Models;

/// <summary>
/// Represents a preview of pending refactoring changes before they are applied.
/// </summary>
public sealed record RefactoringPreviewDto(
    string PreviewToken,
    string Description,
    IReadOnlyList<FileChangeDto> Changes,
    IReadOnlyList<string>? Warnings);
