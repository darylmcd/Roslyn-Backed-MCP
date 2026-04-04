namespace RoslynMcp.Core.Models;

/// <summary>
/// Result of a semantic search, optionally with a note when a fallback strategy was used.
/// </summary>
public sealed record SemanticSearchResponseDto(
    IReadOnlyList<SemanticSearchResultDto> Results,
    string? Warning);
