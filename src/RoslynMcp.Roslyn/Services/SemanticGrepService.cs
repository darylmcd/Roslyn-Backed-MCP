using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// semantic-grep-identifier-scoped-search: token-aware regex search backing the
/// <c>semantic_grep</c> MCP tool. Walks every C# document in the loaded workspace, filters
/// tokens (and trivia for the comment scope) by syntactic kind, and applies the supplied
/// regex against the token text. Capped result count protects against runaway responses
/// on large solutions.
/// </summary>
public sealed class SemanticGrepService : ISemanticGrepService
{
    /// <summary>Hard cap on per-pattern regex evaluation time per document, in milliseconds.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public const string ScopeIdentifiers = "identifiers";
    public const string ScopeStrings = "strings";
    public const string ScopeComments = "comments";
    public const string ScopeAll = "all";

    private static readonly HashSet<string> ValidScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        ScopeIdentifiers, ScopeStrings, ScopeComments, ScopeAll,
    };

    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<SemanticGrepService> _logger;

    public SemanticGrepService(IWorkspaceManager workspace, ILogger<SemanticGrepService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SemanticGrepHitDto>> SearchAsync(
        string workspaceId,
        string pattern,
        string scope,
        string? projectFilter,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("pattern must be non-empty.", nameof(pattern));
        }
        if (string.IsNullOrWhiteSpace(scope) || !ValidScopes.Contains(scope))
        {
            throw new ArgumentException(
                $"scope must be one of: {ScopeIdentifiers}, {ScopeStrings}, {ScopeComments}, {ScopeAll}.",
                nameof(scope));
        }
        if (limit < 1)
        {
            throw new ArgumentException("limit must be >= 1.", nameof(limit));
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regex pattern: {ex.Message}", nameof(pattern), ex);
        }

        var includeIdentifiers = scope is ScopeAll || string.Equals(scope, ScopeIdentifiers, StringComparison.OrdinalIgnoreCase);
        var includeStrings = scope is ScopeAll || string.Equals(scope, ScopeStrings, StringComparison.OrdinalIgnoreCase);
        var includeComments = scope is ScopeAll || string.Equals(scope, ScopeComments, StringComparison.OrdinalIgnoreCase);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var hits = new List<SemanticGrepHitDto>();

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(projectFilter)
                && !string.Equals(project.Name, projectFilter, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();
                if (document.FilePath is null)
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

                CollectFromDocument(
                    root,
                    document.FilePath,
                    regex,
                    includeIdentifiers,
                    includeStrings,
                    includeComments,
                    limit,
                    hits);

                if (hits.Count >= limit)
                {
                    break;
                }
            }

            if (hits.Count >= limit)
            {
                break;
            }
        }

        return hits
            .OrderBy(h => h.FilePath, StringComparer.Ordinal)
            .ThenBy(h => h.Line)
            .ThenBy(h => h.Column)
            .Take(limit)
            .ToList();
    }

    private static void CollectFromDocument(
        SyntaxNode root,
        string filePath,
        Regex regex,
        bool includeIdentifiers,
        bool includeStrings,
        bool includeComments,
        int limit,
        List<SemanticGrepHitDto> hits)
    {
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            if (hits.Count >= limit)
            {
                return;
            }

            var kind = token.Kind();

            if (includeIdentifiers && kind == SyntaxKind.IdentifierToken)
            {
                TryAddMatch(token.ValueText, token.GetLocation(), filePath, "identifier", regex, hits);
            }
            else if (includeStrings && IsStringToken(kind))
            {
                // For string-literal tokens we match against the actual literal value (ValueText
                // strips surrounding quotes/prefix and unescapes), which is what callers most
                // often want when searching for "Console.Write" inside a logged string.
                TryAddMatch(token.ValueText, token.GetLocation(), filePath, "string", regex, hits);
            }

            if (includeComments)
            {
                CollectCommentsFromTriviaList(token.LeadingTrivia, filePath, regex, limit, hits);
                if (hits.Count >= limit) return;
                CollectCommentsFromTriviaList(token.TrailingTrivia, filePath, regex, limit, hits);
                if (hits.Count >= limit) return;
            }
        }
    }

    private static void CollectCommentsFromTriviaList(
        SyntaxTriviaList trivia,
        string filePath,
        Regex regex,
        int limit,
        List<SemanticGrepHitDto> hits)
    {
        foreach (var t in trivia)
        {
            if (hits.Count >= limit) return;
            if (!IsCommentTrivia(t.Kind()))
            {
                continue;
            }
            TryAddMatch(t.ToString(), t.GetLocation(), filePath, "comment", regex, hits);
        }
    }

    private static void TryAddMatch(
        string text,
        Location location,
        string filePath,
        string tokenKind,
        Regex regex,
        List<SemanticGrepHitDto> hits)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        try
        {
            if (!regex.IsMatch(text))
            {
                return;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Skip pathological inputs rather than fail the whole sweep.
            return;
        }

        var lineSpan = location.GetLineSpan();
        // GetLineSpan returns 0-based; expose 1-based to callers (matches every other tool).
        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;

        // Snippet: collapse to single line and trim length so a giant string literal does not
        // bloat the response.
        var snippet = text.Replace('\r', ' ').Replace('\n', ' ');
        if (snippet.Length > 200)
        {
            snippet = snippet[..200];
        }

        hits.Add(new SemanticGrepHitDto(filePath, line, column, tokenKind, snippet));
    }

    private static bool IsStringToken(SyntaxKind kind) => kind switch
    {
        SyntaxKind.StringLiteralToken => true,
        SyntaxKind.InterpolatedStringTextToken => true,
        SyntaxKind.SingleLineRawStringLiteralToken => true,
        SyntaxKind.MultiLineRawStringLiteralToken => true,
        SyntaxKind.Utf8StringLiteralToken => true,
        SyntaxKind.Utf8SingleLineRawStringLiteralToken => true,
        SyntaxKind.Utf8MultiLineRawStringLiteralToken => true,
        SyntaxKind.CharacterLiteralToken => true,
        _ => false,
    };

    private static bool IsCommentTrivia(SyntaxKind kind) => kind switch
    {
        SyntaxKind.SingleLineCommentTrivia => true,
        SyntaxKind.MultiLineCommentTrivia => true,
        SyntaxKind.SingleLineDocumentationCommentTrivia => true,
        SyntaxKind.MultiLineDocumentationCommentTrivia => true,
        _ => false,
    };
}
