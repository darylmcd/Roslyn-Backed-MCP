namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of a semantic search, optionally with a note when a fallback strategy was used.
/// When <paramref name="Debug"/> is non-null the caller can see which tokens and predicates
/// the parser derived from the raw query — useful when a verbose natural-language query
/// returns zero results and the caller needs to understand why.
/// </summary>
public sealed record SemanticSearchResponseDto(
    IReadOnlyList<SemanticSearchResultDto> Results,
    string? Warning,
    SemanticSearchDebugDto? Debug = null);

/// <summary>
/// Diagnostic payload emitted alongside a semantic_search response when the query either
/// produced no results or fell back to a less-precise matching strategy. Helps callers
/// decode why a verbose natural-language query did not match (backlog row
/// <c>semantic-search-zero-results-verbose-query</c>).
/// </summary>
/// <param name="ParsedTokens">
/// The stemmed keyword tokens derived from the raw query after stopword removal.
/// Empty when the query contained no significant words.
/// </param>
/// <param name="AppliedPredicates">
/// Human-readable names of the structured predicates the parser matched on the query
/// (e.g. <c>"async-keyword"</c>, <c>"returning-Task&lt;bool&gt;"</c>, <c>"implementing-IDisposable"</c>).
/// Empty when no structured predicate matched.
/// </param>
/// <param name="FallbackStrategy">
/// The strategy used to compute the final result set. One of:
/// <c>"structured"</c> (predicates matched directly),
/// <c>"name-substring"</c> (single-term contains-match over symbol names),
/// <c>"token-or-match"</c> (verbose-query fallback: any token is a substring of the symbol name),
/// <c>"none"</c> (no results from any strategy).
/// </param>
public sealed record SemanticSearchDebugDto(
    IReadOnlyList<string> ParsedTokens,
    IReadOnlyList<string> AppliedPredicates,
    string FallbackStrategy);
