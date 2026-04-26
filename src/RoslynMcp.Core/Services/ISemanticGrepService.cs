using RoslynMcp.Core.Models;

namespace RoslynMcp.Core.Services;

/// <summary>
/// Token-aware regex search across a loaded workspace. Walks each document's syntax tokens
/// and trivia, filters by syntactic category, and returns regex matches as ranked hits.
/// </summary>
/// <remarks>
/// semantic-grep-identifier-scoped-search: backs the <c>semantic_grep</c> MCP tool. Lets
/// callers exclude string-literal and comment matches that plain text grep would return.
/// </remarks>
public interface ISemanticGrepService
{
    /// <summary>
    /// Walks every document in the workspace, filters tokens/trivia by <paramref name="scope"/>,
    /// runs <paramref name="pattern"/> as a .NET regex against each candidate's text, and
    /// returns matching hits up to <paramref name="limit"/>.
    /// </summary>
    /// <param name="workspaceId">Workspace session identifier.</param>
    /// <param name="pattern">.NET regex pattern. Must be non-empty.</param>
    /// <param name="scope">
    /// One of <c>identifiers</c> (identifier tokens only), <c>strings</c> (string-literal
    /// tokens only — includes verbatim, interpolated text components, and raw strings),
    /// <c>comments</c> (single-line and multi-line comment trivia), or <c>all</c> (the
    /// union of all three buckets).
    /// </param>
    /// <param name="projectFilter">
    /// Optional case-sensitive project name filter. When supplied, only documents owned by
    /// projects whose <see cref="Microsoft.CodeAnalysis.Project.Name"/> matches are walked.
    /// </param>
    /// <param name="limit">
    /// Hard cap on the number of returned hits (default 500). The token walk stops as soon
    /// as the cap is reached so large solutions cannot produce runaway responses.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Hits in deterministic order: ascending file path, then ascending line, then ascending
    /// column. The list length never exceeds <paramref name="limit"/>.
    /// </returns>
    Task<IReadOnlyList<SemanticGrepHitDto>> SearchAsync(
        string workspaceId,
        string pattern,
        string scope,
        string? projectFilter,
        int limit,
        CancellationToken ct);
}
