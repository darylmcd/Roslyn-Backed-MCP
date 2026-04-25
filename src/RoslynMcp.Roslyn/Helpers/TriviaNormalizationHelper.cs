using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared helpers for normalizing whitespace/trivia after syntax mutations
/// (removals, moves). Used by organize-usings and type-move paths to avoid
/// stray blank lines left behind by <see cref="SyntaxRemoveOptions.KeepExteriorTrivia"/>
/// and artifacts introduced by <see cref="SyntaxNodeExtensions.NormalizeWhitespace"/>.
/// </summary>
internal static class TriviaNormalizationHelper
{
    /// <summary>
    /// Strips leading whitespace and end-of-line trivia from the file's first token.
    /// Use after a removal that may have left a blank line at the head of the file.
    /// </summary>
    public static CompilationUnitSyntax NormalizeLeadingTrivia(CompilationUnitSyntax root)
    {
        var firstToken = root.GetFirstToken();
        if (!firstToken.LeadingTrivia.Any()) return root;

        var cleaned = SyntaxFactory.TriviaList(
            firstToken.LeadingTrivia.SkipWhile(t =>
                t.IsKind(SyntaxKind.EndOfLineTrivia) || t.IsKind(SyntaxKind.WhitespaceTrivia)));

        return root.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(cleaned));
    }

    /// <summary>
    /// Ensures exactly one blank line between the last using directive and the first
    /// member declaration. Preserves XML doc comments and regular comments attached to
    /// the first member by skipping ONLY whitespace and end-of-line trivia at the head.
    /// </summary>
    public static CompilationUnitSyntax NormalizeUsingToMemberSeparator(CompilationUnitSyntax root)
    {
        if (root.Usings.Count == 0 || root.Members.Count == 0) return root;

        var firstMember = root.Members.First();
        var firstToken = firstMember.GetFirstToken();
        var leading = firstToken.LeadingTrivia;

        // Skip ONLY whitespace/EOL trivia at the head; keep doc comments, regular comments,
        // and pragma/region directives.
        var preserved = leading.SkipWhile(t =>
            t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia)).ToList();

        // Two newlines = one blank line between the last using and the first member.
        var rebuilt = SyntaxFactory.TriviaList(
            SyntaxFactory.CarriageReturnLineFeed,
            SyntaxFactory.CarriageReturnLineFeed)
            .AddRange(preserved);

        return root.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(rebuilt));
    }

    /// <summary>
    /// Collapses runs of 2+ consecutive end-of-line trivia in each using directive's
    /// trailing trivia down to a single end-of-line. No-op when there are no usings.
    /// Use after <see cref="SyntaxNode.RemoveNodes{TNode}"/> with
    /// <see cref="SyntaxRemoveOptions.KeepExteriorTrivia"/> to flatten the blank lines
    /// left behind.
    /// </summary>
    public static CompilationUnitSyntax CollapseBlankLinesInUsingBlock(CompilationUnitSyntax root)
    {
        if (root.Usings.Count == 0) return root;

        var newUsings = root.Usings.Select(u =>
        {
            var trailing = u.GetTrailingTrivia();
            var collapsed = CollapseConsecutiveEol(trailing);
            return u.WithTrailingTrivia(collapsed);
        }).ToList();

        return root.WithUsings(SyntaxFactory.List(newUsings));
    }

    private static SyntaxTriviaList CollapseConsecutiveEol(SyntaxTriviaList input)
    {
        var result = new List<SyntaxTrivia>(input.Count);
        var consecutiveEol = 0;
        foreach (var trivia in input)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                consecutiveEol++;
                if (consecutiveEol <= 1) result.Add(trivia);
            }
            else
            {
                consecutiveEol = 0;
                result.Add(trivia);
            }
        }
        return SyntaxFactory.TriviaList(result);
    }

    /// <summary>
    /// Removes "orphan-indent" whitespace trivia: <see cref="SyntaxKind.WhitespaceTrivia"/>
    /// items sandwiched between two <see cref="SyntaxKind.EndOfLineTrivia"/> items, which
    /// render as a line containing only whitespace. After
    /// <see cref="SyntaxNode.RemoveNodes{TNode}"/> with
    /// <see cref="SyntaxRemoveOptions.KeepExteriorTrivia"/>, the indentation that preceded
    /// a removed member is preserved as such an orphan, leaving visible trailing-whitespace
    /// lines in the output. This rewriter walks every token in the subtree and rebuilds
    /// each token's leading and trailing trivia without those orphans. It NEVER touches
    /// whitespace that immediately precedes a token (real indentation), so intentional
    /// indentation inside preserved members is left alone.
    /// </summary>
    public static TNode RemoveOrphanIndentTrivia<TNode>(TNode node) where TNode : SyntaxNode
    {
        return (TNode)new OrphanIndentRewriter().Visit(node);
    }

    private sealed class OrphanIndentRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var newLeading = TryStripOrphanIndent(token.LeadingTrivia);
            var newTrailing = TryStripOrphanIndent(token.TrailingTrivia);
            if (newLeading is { } leading)
            {
                token = token.WithLeadingTrivia(leading);
            }
            if (newTrailing is { } trailing)
            {
                token = token.WithTrailingTrivia(trailing);
            }
            return base.VisitToken(token);
        }
    }

    /// <summary>
    /// Returns a trivia list with all whitespace-only lines collapsed to bare end-of-line trivia,
    /// or <c>null</c> when no orphans are present (allowing the caller to skip a token rewrite).
    /// A whitespace-only line is a <see cref="SyntaxKind.WhitespaceTrivia"/> that is followed by
    /// an <see cref="SyntaxKind.EndOfLineTrivia"/> (i.e. it is not the indentation that precedes
    /// a real token). <see cref="SyntaxTriviaList"/> is a value type, so a <c>null</c> sentinel
    /// is used instead of reference equality (CA2013).
    /// </summary>
    private static SyntaxTriviaList? TryStripOrphanIndent(SyntaxTriviaList input)
    {
        var rebuilt = (List<SyntaxTrivia>?)null;
        for (var i = 0; i < input.Count; i++)
        {
            var trivia = input[i];
            var isOrphanIndent = trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && i + 1 < input.Count
                && input[i + 1].IsKind(SyntaxKind.EndOfLineTrivia);
            if (isOrphanIndent)
            {
                rebuilt ??= new List<SyntaxTrivia>(input.Take(i));
                // Skip the orphan whitespace; the following EOL is preserved by the next iteration.
                continue;
            }
            rebuilt?.Add(trivia);
        }
        return rebuilt is null ? null : SyntaxFactory.TriviaList(rebuilt);
    }
}
