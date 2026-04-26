namespace RoslynMcp.Core.Models;

/// <summary>
/// Single hit returned by the <c>semantic_grep</c> MCP tool — one row per token whose
/// text matches the supplied regex pattern, scoped by syntactic category (identifier vs
/// string literal vs comment trivia).
/// </summary>
/// <remarks>
/// semantic-grep-identifier-scoped-search: produced by <c>semantic_grep</c>. The token
/// walk filters by <see cref="Microsoft.CodeAnalysis.SyntaxToken"/> kind so callers can
/// avoid false-positive matches that plain text grep returns inside string literals or
/// comments. <see cref="TokenKind"/> is one of <c>identifier</c>, <c>string</c>, or
/// <c>comment</c>.
/// </remarks>
/// <param name="FilePath">Absolute path of the source file containing the hit.</param>
/// <param name="Line">1-based line number of the matching token.</param>
/// <param name="Column">1-based column number of the matching token.</param>
/// <param name="TokenKind">
/// Bucketed kind of the matched token: <c>identifier</c>, <c>string</c>, or <c>comment</c>.
/// </param>
/// <param name="Snippet">Single-line excerpt containing the match (token text or trivia text).</param>
public sealed record SemanticGrepHitDto(
    string FilePath,
    int Line,
    int Column,
    string TokenKind,
    string Snippet);
