using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class SymbolNavigationService : ISymbolNavigationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SymbolNavigationService> _logger;

    public SymbolNavigationService(IWorkspaceManager workspace, ILogger<SymbolNavigationService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        symbol = symbol.OriginalDefinition;

        var results = new List<LocationDto>();
        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var doc = solution.GetDocument(location.SourceTree!);
            var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
            results.Add(SymbolMapper.ToLocationDto(location, symbol, preview, "Definition"));
        }

        return results;
    }

    public async Task<IReadOnlyList<LocationDto>> GoToTypeDefinitionAsync(string workspaceId, SymbolLocator locator, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var symbol = await SymbolResolver.ResolveAsync(solution, locator, ct).ConfigureAwait(false);
        if (symbol is null) return [];

        // goto-type-definition-local-vars: extract ITypeSymbol (not just INamedTypeSymbol) so
        // arrays, type parameters, and pointers carry through. Empty result for primitives is
        // still handled by the SpecialType guard below.
        ITypeSymbol? typeSymbol = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol prop => prop.Type,
            IEventSymbol evt => evt.Type,
            IMethodSymbol method => method.ReturnType,
            ITypeSymbol type => type,
            _ => null
        };

        if (typeSymbol is null) return [];

        var results = new List<LocationDto>();
        var seenLocations = new HashSet<Location>();

        // goto-type-definition-local-vars: walk constructed-generic type arguments when the
        // outer type is in metadata (e.g. IEnumerable<UserDto> → UserDto). Also unwrap arrays
        // and pointers to their element type so `var users = new UserDto[]{}` navigates to
        // UserDto.
        foreach (var candidate in EnumerateTypeCandidates(typeSymbol))
        {
            foreach (var location in candidate.Locations.Where(l => l.IsInSource))
            {
                if (!seenLocations.Add(location)) continue;
                var doc = solution.GetDocument(location.SourceTree!);
                var preview = doc is not null ? await SymbolResolver.GetPreviewTextAsync(doc, location, ct).ConfigureAwait(false) : null;
                results.Add(SymbolMapper.ToLocationDto(location, candidate, preview, "Definition"));
            }
        }

        if (results.Count > 0)
        {
            return results;
        }

        // Provide descriptive message when nothing in the type's hierarchy has a source location
        // (built-in/primitive types, BCL generics whose arguments are also in metadata, etc.).
        throw new InvalidOperationException(
            $"Cannot navigate to type definition for '{typeSymbol.ToDisplayString()}' — " +
            "neither the type nor any of its type arguments are defined in source. " +
            "This typically means the type is defined in the .NET runtime or an external assembly.");
    }

    /// <summary>
    /// Yields the type itself, then peels off layers Roslyn does NOT collapse via
    /// <see cref="ISymbol.Locations"/> — array element type, pointer pointed-at type, and
    /// constructed generic type arguments — so callers can navigate from a variable typed
    /// <c>UserDto[]</c> or <c>IEnumerable&lt;UserDto&gt;</c> to <c>UserDto</c>'s source.
    /// </summary>
    private static IEnumerable<ITypeSymbol> EnumerateTypeCandidates(ITypeSymbol root)
    {
        var queue = new Queue<ITypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;

            switch (current)
            {
                case IArrayTypeSymbol array:
                    queue.Enqueue(array.ElementType);
                    break;
                case IPointerTypeSymbol pointer:
                    queue.Enqueue(pointer.PointedAtType);
                    break;
                case INamedTypeSymbol named when named.IsGenericType:
                    foreach (var arg in named.TypeArguments)
                        queue.Enqueue(arg);
                    break;
            }
        }
    }

    public async Task<SymbolDto?> GetEnclosingSymbolAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return null;

        var text = await syntaxTree.GetTextAsync(ct).ConfigureAwait(false);
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

        var position = text.Lines[line - 1].Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);
        var node = root.FindToken(position).Parent;

        while (node is not null)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(node, ct);
                if (declaredSymbol is not null)
                    return SymbolMapper.ToDto(declaredSymbol, solution);
            }
            node = node.Parent;
        }

        return null;
    }

    /// <summary>
    /// position-probe-for-test-fixture-authoring: returns a trivia-aware snapshot of the token at
    /// the caret. Distinguishes "caret on the token" from "caret inside the leading trivia of the
    /// next token" via <see cref="PositionProbeDto.LeadingTriviaBefore"/> so test-fixture authors can
    /// verify their hard-coded line/column anchors without re-implementing Roslyn's token-boundary
    /// rules. Never applies the adjacent-identifier fallback used by
    /// <see cref="SymbolResolver.ResolveAtPositionAsync"/> in lenient mode — whitespace stays
    /// whitespace.
    /// </summary>
    public async Task<PositionProbeDto?> ProbePositionAsync(string workspaceId, string filePath, int line, int column, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (syntaxTree is null) return null;

        var text = await syntaxTree.GetTextAsync(ct).ConfigureAwait(false);
        if (line < 1 || line > text.Lines.Count)
        {
            throw new ArgumentException(
                $"Line {line} is out of range. The file has {text.Lines.Count} line(s).",
                nameof(line));
        }

        var lineInfo = text.Lines[line - 1];
        // Column 1-based; clamp to line-end so callers probing past the last column on a line fall
        // on the end-of-line trivia (deterministic) instead of silently advancing to the next line's
        // first token — this is the end-of-line risk called out in the plan's Risks field.
        var maxColumn = lineInfo.EndIncludingLineBreak - lineInfo.Start + 1;
        if (column < 1 || column > maxColumn)
        {
            throw new ArgumentException(
                $"Column {column} is out of range for line {line} (valid range: 1..{maxColumn}).",
                nameof(column));
        }

        var position = lineInfo.Start + (column - 1);
        var root = await syntaxTree.GetRootAsync(ct).ConfigureAwait(false);

        // FindToken returns the token whose FullSpan encloses `position`. That FullSpan covers:
        //   [leading-trivia...][token.Span][trailing-trivia...]
        // Roslyn's convention attaches trailing whitespace (up to and including the terminating
        // end-of-line trivia) to the preceding token, NOT the following token's leading trivia.
        // So for a caret in "    public void MakeThemSpeak(    IEnumerable…" at column 32 (inside
        // the 4-space run between '(' and 'IEnumerable'), FindToken returns '(' and position is
        // inside '(''s trailing trivia, not 'IEnumerable''s leading trivia.
        var token = root.FindToken(position);

        // End-of-file edge case: FindToken returns the EndOfFileToken with empty text; classify
        // explicitly so callers see a stable shape instead of an Other+empty-text combo.
        if (token.IsKind(SyntaxKind.EndOfFileToken))
        {
            var eofContaining = ResolveContainingSymbol(semanticModel, token, ct);
            return new PositionProbeDto(
                FilePath: filePath,
                Line: line,
                Column: column,
                TokenKind: "EndOfFile",
                SyntaxKind: nameof(SyntaxKind.EndOfFileToken),
                TokenText: string.Empty,
                ContainingSymbol: eofContaining.Display,
                ContainingSymbolKind: eofContaining.Kind,
                LeadingTriviaBefore: false);
        }

        // Trivia classification:
        //   position < token.SpanStart        → inside the token's LEADING trivia
        //   position >= token.Span.End        → inside the token's TRAILING trivia
        //   otherwise                          → on the token proper
        // leadingTriviaBefore reports ONLY the first case — trailing trivia is its own bucket
        // reported via tokenKind/syntaxKind but not via the leading-trivia flag. This lets callers
        // distinguish "caret is before the next token (no token seen yet)" from "caret is after
        // the previous token (already consumed it)" which matters for certain fixture patterns
        // (e.g., testing that a refactoring preserves trailing whitespace of the preceding token).
        var inLeadingTrivia = position < token.SpanStart;
        var inTrailingTrivia = position >= token.Span.End;

        string tokenKind;
        string syntaxKind;
        string tokenText;

        if (inLeadingTrivia || inTrailingTrivia)
        {
            // Walk the correct trivia list and find the specific trivia span the caret is inside.
            // This gives a finer classification (WhitespaceTrivia vs SingleLineCommentTrivia vs
            // EndOfLineTrivia) than just reporting "trivia".
            var triviaList = inLeadingTrivia ? token.LeadingTrivia : token.TrailingTrivia;
            var trivia = triviaList.FirstOrDefault(t => t.FullSpan.Contains(position));
            var triviaIsNone = trivia.IsKind(SyntaxKind.None);
            syntaxKind = triviaIsNone
                ? (inLeadingTrivia ? "LeadingTrivia" : "TrailingTrivia")
                : trivia.Kind().ToString();
            tokenKind = ClassifyTrivia(trivia);
            tokenText = triviaIsNone ? string.Empty : trivia.ToFullString();
        }
        else
        {
            syntaxKind = token.Kind().ToString();
            tokenKind = ClassifyToken(token);
            tokenText = token.Text;
        }

        var containing = ResolveContainingSymbol(semanticModel, token, ct);
        return new PositionProbeDto(
            FilePath: filePath,
            Line: line,
            Column: column,
            TokenKind: tokenKind,
            SyntaxKind: syntaxKind,
            TokenText: tokenText,
            ContainingSymbol: containing.Display,
            ContainingSymbolKind: containing.Kind,
            LeadingTriviaBefore: inLeadingTrivia);
    }

    /// <summary>
    /// Classifies a <see cref="SyntaxToken"/> into a stable friendly bucket: Identifier, Keyword,
    /// Punctuation, Literal, or Other. Kept separate from trivia classification so the two never
    /// cross-pollute.
    /// </summary>
    private static string ClassifyToken(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.IdentifierToken)) return "Identifier";
        if (SyntaxFacts.IsKeywordKind(token.Kind()) || SyntaxFacts.IsContextualKeyword(token.Kind())) return "Keyword";
        if (SyntaxFacts.IsPunctuation(token.Kind())) return "Punctuation";
        if (SyntaxFacts.IsAnyToken(token.Kind()) && IsLiteralKind(token.Kind())) return "Literal";
        return "Other";
    }

    private static bool IsLiteralKind(SyntaxKind kind) => kind is
        SyntaxKind.NumericLiteralToken or
        SyntaxKind.StringLiteralToken or
        SyntaxKind.CharacterLiteralToken or
        SyntaxKind.InterpolatedStringTextToken or
        SyntaxKind.SingleLineRawStringLiteralToken or
        SyntaxKind.MultiLineRawStringLiteralToken or
        SyntaxKind.Utf8StringLiteralToken or
        SyntaxKind.Utf8SingleLineRawStringLiteralToken or
        SyntaxKind.Utf8MultiLineRawStringLiteralToken;

    /// <summary>
    /// Classifies a <see cref="SyntaxTrivia"/> into a stable friendly bucket: Whitespace, Comment,
    /// EndOfLine, or Other. Documentation-comment trivia is bucketed as Comment alongside regular
    /// comments — fine-grained distinction is still available via the raw SyntaxKind field.
    /// </summary>
    private static string ClassifyTrivia(SyntaxTrivia trivia) => trivia.Kind() switch
    {
        SyntaxKind.WhitespaceTrivia => "Whitespace",
        SyntaxKind.EndOfLineTrivia => "EndOfLine",
        SyntaxKind.SingleLineCommentTrivia => "Comment",
        SyntaxKind.MultiLineCommentTrivia => "Comment",
        SyntaxKind.SingleLineDocumentationCommentTrivia => "Comment",
        SyntaxKind.MultiLineDocumentationCommentTrivia => "Comment",
        SyntaxKind.DocumentationCommentExteriorTrivia => "Comment",
        SyntaxKind.None => "Other",
        _ => "Other"
    };

    /// <summary>
    /// Walks the token's ancestor chain looking for the nearest enclosing member, local function,
    /// or type declaration — matches the resolution used by <see cref="GetEnclosingSymbolAsync"/>
    /// so <c>probe_position</c> and <c>enclosing_symbol</c> stay consistent.
    /// </summary>
    private static (string? Display, string? Kind) ResolveContainingSymbol(SemanticModel semanticModel, SyntaxToken token, CancellationToken ct)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax)
            {
                var declared = semanticModel.GetDeclaredSymbol(node, ct);
                if (declared is not null)
                {
                    return (declared.ToDisplayString(), declared.Kind.ToString());
                }
            }
        }

        return (null, null);
    }
}
