namespace Company.RoslynMcp.Core.Models;

/// <summary>
/// Represents a test case related to one or more source files.
/// </summary>
public sealed record RelatedTestCaseDto(
    string DisplayName,
    string FullyQualifiedName,
    string ProjectName,
    string? FilePath,
    int? Line,
    IReadOnlyList<string> TriggeredByFiles);

/// <summary>
/// Represents the related tests and filter expression for a set of files.
/// </summary>
public sealed record RelatedTestsForFilesDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter);
