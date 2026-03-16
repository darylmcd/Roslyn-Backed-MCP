namespace Company.RoslynMcp.Core.Models;

public sealed record RelatedTestCaseDto(
    string DisplayName,
    string FullyQualifiedName,
    string ProjectName,
    string? FilePath,
    int? Line,
    IReadOnlyList<string> TriggeredByFiles);

public sealed record RelatedTestsForFilesDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter);
