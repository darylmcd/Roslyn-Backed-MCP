namespace RoslynMcp.Core.Models;

/// <summary>
/// position-probe-for-test-fixture-authoring: raw lexical + containing-symbol state at a source
/// position. Intended for test-fixture authoring and caret diagnostics — eliminates the off-by-one
/// column-counting friction encountered when hard-coding <c>SymbolLocator.BySource(path, line, column)</c>
/// anchors. Shape is deterministic and trivia-aware so callers can distinguish "caret on identifier"
/// from "caret inside whitespace between tokens" without re-implementing Roslyn's token-boundary rules.
/// </summary>
/// <param name="FilePath">Absolute source-file path of the probed position (normalized by Roslyn).</param>
/// <param name="Line">1-based line the caret lands on.</param>
/// <param name="Column">1-based column the caret lands on.</param>
/// <param name="TokenKind">
/// Logical classification of what the caret sits on:
/// <c>Identifier</c>, <c>Keyword</c>, <c>Punctuation</c>, <c>Literal</c>, <c>Whitespace</c>, <c>Comment</c>,
/// <c>EndOfLine</c>, <c>EndOfFile</c>, or <c>Other</c>. Whitespace / comment / end-of-line are only
/// reported when the caret sits inside that trivia — a caret on the token itself always returns the
/// token's own classification regardless of surrounding trivia.
/// </param>
/// <param name="SyntaxKind">
/// Raw Roslyn <see cref="Microsoft.CodeAnalysis.CSharp.SyntaxKind"/> name for the underlying token or
/// trivia node (e.g. <c>IdentifierToken</c>, <c>WhitespaceTrivia</c>, <c>EndOfLineTrivia</c>). Useful
/// for fixture authors who need to assert on the exact lexical shape the compiler sees.
/// </param>
/// <param name="TokenText">
/// Literal text of the token (or trivia) the caret is on, verbatim as it appears in source. Empty
/// string when the caret is at end-of-file.
/// </param>
/// <param name="ContainingSymbol">
/// Fully-qualified display name of the nearest enclosing member, type, or local function — the same
/// resolution <see cref="ISymbolNavigationService.GetEnclosingSymbolAsync"/> returns. <see langword="null"/>
/// only when the position is outside any declaration (e.g. top-level-statement host files with only
/// file-scoped usings).
/// </param>
/// <param name="ContainingSymbolKind">
/// Kind of the containing symbol as a friendly string (<c>Method</c>, <c>Property</c>, <c>Class</c>,
/// <c>LocalFunction</c>, etc.), or <see langword="null"/> when <paramref name="ContainingSymbol"/> is null.
/// </param>
/// <param name="LeadingTriviaBefore">
/// <see langword="true"/> when the caret sits inside the leading trivia of the token returned by
/// <see cref="Microsoft.CodeAnalysis.SyntaxNode.FindToken"/> — i.e. the caret is positioned BEFORE
/// the token's <see cref="Microsoft.CodeAnalysis.SyntaxToken.SpanStart"/> but inside its full span
/// (leading whitespace, comments, and blank lines attached to the NEXT token). When the caret
/// instead lands in trailing trivia (whitespace / end-of-line attached to the PREVIOUS token per
/// Roslyn's trivia-attachment rules) <paramref name="TokenKind"/> still reports Whitespace /
/// EndOfLine / Comment, but this flag stays <see langword="false"/>. Callers that only care
/// "is the caret inside any trivia at all?" should check <c>TokenKind</c> against
/// <c>Whitespace</c>/<c>EndOfLine</c>/<c>Comment</c>; those who want to distinguish the two
/// directions use this flag.
/// </param>
public sealed record PositionProbeDto(
    string FilePath,
    int Line,
    int Column,
    string TokenKind,
    string SyntaxKind,
    string TokenText,
    string? ContainingSymbol,
    string? ContainingSymbolKind,
    bool LeadingTriviaBefore);
