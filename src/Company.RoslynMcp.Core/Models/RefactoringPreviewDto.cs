namespace Company.RoslynMcp.Core.Models;

public sealed record RefactoringPreviewDto(
    string PreviewToken,
    string Description,
    IReadOnlyList<FileChangeDto> Changes,
    IReadOnlyList<string>? Warnings);
