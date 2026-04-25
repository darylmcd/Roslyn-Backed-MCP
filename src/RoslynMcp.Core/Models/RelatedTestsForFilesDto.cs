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
/// Explainability envelope for <see cref="RelatedTestsForFilesDto"/>. Lets callers
/// distinguish "no tests exist for this file set" (no test projects scanned, or no
/// heuristics produced any candidates) from "tests exist but the heuristic missed them"
/// (e.g. file paths did not resolve to workspace documents, or no type/file-name search
/// terms matched any discovered test).
/// </summary>
/// <param name="ScannedTestProjects">
/// Number of test projects walked during discovery. Zero means the workspace contains no
/// test projects — empty results are then expected.
/// </param>
/// <param name="HeuristicsAttempted">
/// Names of name-matching heuristics that were applied (e.g. <c>"type-name"</c>,
/// <c>"file-name"</c>). Empty when no input file resolved to a document and therefore no
/// heuristic ran.
/// </param>
/// <param name="MissReasons">
/// Per-input-file reasons explaining why no related tests were attributed to that file.
/// Populated only when the file produced zero matches; e.g.
/// <c>"path 'Foo.cs' did not resolve to a workspace document"</c> or
/// <c>"no test method name/path matched any of [Foo, Bar]"</c>. Empty when every input
/// file produced at least one match.
/// </param>
public sealed record RelatedTestsDiagnosticsDto(
    int ScannedTestProjects,
    IReadOnlyList<string> HeuristicsAttempted,
    IReadOnlyList<string> MissReasons);

/// <summary>
/// Represents the related tests and filter expression for a set of changed files.
/// </summary>
/// <remarks>
/// test-related-response-envelope-parity: <c>test_related</c> and <c>test_related_files</c>
/// share this envelope shape (<c>tests</c>, <c>dotnetTestFilter</c>, <c>pagination</c>) so
/// callers can route either response through the same downstream filter +
/// <c>test_run --filter</c> pipeline.
/// </remarks>
/// <param name="Tests">Related test cases (page).</param>
/// <param name="DotnetTestFilter">Composite <c>dotnet test --filter</c> expression for the page.</param>
/// <param name="Pagination">Total / returned / has-more pagination envelope.</param>
/// <param name="Diagnostics">
/// test-related-files-empty-result-explainability: explainability metadata so an empty
/// <see cref="Tests"/> list is not ambiguous between "no tests exist" and "heuristic miss."
/// </param>
public sealed record RelatedTestsForFilesDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter,
    PaginationInfo Pagination,
    RelatedTestsDiagnosticsDto Diagnostics);

/// <summary>
/// Represents the related tests and filter expression for a single symbol. Mirrors
/// <see cref="RelatedTestsForFilesDto"/> envelope shape — <c>TriggeredByFiles</c> is empty
/// for symbol-mode results because the trigger is the symbol itself, not a changed file.
/// </summary>
/// <remarks>
/// test-related-files-empty-result-explainability: <c>Diagnostics</c> mirrors
/// <see cref="RelatedTestsForFilesDto.Diagnostics"/> so the
/// <c>test-related-response-envelope-parity</c> contract (identical envelope shape across
/// symbol-mode and files-mode) is preserved. <c>MissReasons</c> here describes why the
/// symbol query found no related tests (e.g. "symbol resolved but no test name matched
/// terms [Foo, Bar]" or "symbol did not resolve to any workspace symbol").
/// </remarks>
public sealed record RelatedTestsForSymbolDto(
    IReadOnlyList<RelatedTestCaseDto> Tests,
    string DotnetTestFilter,
    PaginationInfo Pagination,
    RelatedTestsDiagnosticsDto Diagnostics);
