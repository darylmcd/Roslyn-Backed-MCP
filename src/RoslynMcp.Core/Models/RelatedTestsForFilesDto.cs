namespace RoslynMcp.Core.Models;

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
/// Pagination envelope shared by <see cref="RelatedTestsForFilesDto"/> and
/// <see cref="RelatedTestsForSymbolDto"/>. <c>Total</c> is the count before <c>maxResults</c>
/// truncation; <c>Returned</c> is the size of the <c>Tests</c> list; <c>HasMore</c> is
/// <see langword="true"/> when <c>Total &gt; Returned</c>.
/// </summary>
public sealed record PaginationInfo(
    int Total,
    int Returned,
    bool HasMore);

/// <summary>
/// Represents the related tests and filter expression for a set of changed files.
/// </summary>
/// <remarks>
/// test-related-response-envelope-parity: <c>test_related</c> and <c>test_related_files</c>
/// share this envelope shape (<c>tests</c>, <c>dotnetTestFilter</c>, <c>pagination</c>) so
/// callers can route either response through the same downstream filter +
/// <c>test_run --filter</c> pipeline.
/// </remarks>
public sealed record RelatedTestsForFilesDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter,
    PaginationInfo Pagination);

/// <summary>
/// Represents the related tests and filter expression for a single symbol. Mirrors
/// <see cref="RelatedTestsForFilesDto"/> envelope shape — <c>TriggeredByFiles</c> is empty
/// for symbol-mode results because the trigger is the symbol itself, not a changed file.
/// </summary>
public sealed record RelatedTestsForSymbolDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter,
    PaginationInfo Pagination);
