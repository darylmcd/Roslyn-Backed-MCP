using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 6 implementation. Pattern grammar:
/// <list type="bullet">
///   <item><description><c>__name__</c> (two underscores on each side) is a capture placeholder that matches any node of compatible kind.</description></item>
///   <item><description>Everything else must match structurally via <see cref="SyntaxFactory.AreEquivalent(SyntaxNode?, SyntaxNode?, bool)"/> (trivia ignored).</description></item>
///   <item><description>Pattern and goal are parsed as expressions first; if that fails, the service falls back to parsing them as statements.</description></item>
/// </list>
/// </summary>
public sealed class RestructureService : IRestructureService
{
    // A placeholder token is two leading + two trailing underscores around [a-zA-Z][a-zA-Z0-9_]*
    // and never contains more than one contiguous word. We accept the lighter __FOO pattern
    // (matches python-refactor's convention) while also allowing __foo, __bar_42__.
    private static readonly Regex PlaceholderPattern =
        new(@"__(?<name>[A-Za-z][A-Za-z0-9_]*)__", RegexOptions.Compiled);

    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public RestructureService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewRestructureAsync(
        string workspaceId, string pattern, string goal, RestructureScope scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("pattern must be non-empty.", nameof(pattern));
        if (goal is null)
            throw new ArgumentException("goal must be non-null (use empty string to delete matches).", nameof(goal));

        var (patternNode, patternKind) = ParsePatternOrGoal(pattern, "pattern");
        var (goalNode, goalKind) = ParsePatternOrGoal(goal, "goal");
        if (patternKind != goalKind)
        {
            throw new ArgumentException(
                $"pattern and goal must be the same syntactic kind (both expressions or both statements). " +
                $"pattern={patternKind}, goal={goalKind}.");
        }

        var placeholderNames = ExtractPlaceholderNames(patternNode);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var accumulator = solution;
        var changes = new List<FileChangeDto>();
        var totalMatches = 0;

        foreach (var project in EnumerateProjects(solution, scope))
        {
            foreach (var document in EnumerateDocuments(project, scope))
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var rewriter = new StructuralRewriter(patternNode, goalNode, placeholderNames);
                var newRoot = rewriter.Visit(root);

                if (rewriter.MatchCount == 0 || newRoot is null) continue;
                totalMatches += rewriter.MatchCount;

                var newText = newRoot.ToFullString();
                var oldText = root.ToFullString();

                accumulator = accumulator.WithDocumentText(document.Id, Microsoft.CodeAnalysis.Text.SourceText.From(newText));
                var docPath = document.FilePath ?? document.Name;
                changes.Add(new FileChangeDto(
                    FilePath: docPath,
                    UnifiedDiff: DiffGenerator.GenerateUnifiedDiff(oldText, newText, docPath)));
            }
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException(
                $"restructure_preview: no matches found for pattern in scope. " +
                $"Verify pattern kind ({patternKind}), placeholder names, and scope filters.");
        }

        var description = $"Restructure {totalMatches} match(es) across {changes.Count} file(s)";
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static (SyntaxNode Node, string Kind) ParsePatternOrGoal(string text, string argName)
    {
        // Try expression first (most common case per the backlog examples); fall back to statement.
        var exprTree = SyntaxFactory.ParseExpression(text, consumeFullText: true);
        if (!exprTree.ContainsDiagnostics)
        {
            return (exprTree, "expression");
        }

        var stmt = SyntaxFactory.ParseStatement(text, consumeFullText: true);
        if (!stmt.ContainsDiagnostics)
        {
            return (stmt, "statement");
        }

        throw new ArgumentException(
            $"{argName} could not be parsed as either an expression or a statement. " +
            $"Fix the syntax and retry. Pattern text: {Truncate(text, 200)}",
            argName);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    private static IReadOnlyCollection<string> ExtractPlaceholderNames(SyntaxNode pattern)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in pattern.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var match = PlaceholderPattern.Match(identifier.Identifier.ValueText);
            if (match.Success && match.Length == identifier.Identifier.ValueText.Length)
            {
                names.Add(match.Groups["name"].Value);
            }
        }
        return names;
    }

    private static IEnumerable<Project> EnumerateProjects(Solution solution, RestructureScope scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.ProjectName))
        {
            var match = solution.Projects.FirstOrDefault(p =>
                string.Equals(p.Name, scope.ProjectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.FilePath, scope.ProjectName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException($"Project '{scope.ProjectName}' not found in workspace.");
            return [match];
        }
        return solution.Projects;
    }

    private static IEnumerable<Document> EnumerateDocuments(Project project, RestructureScope scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.FilePath))
        {
            var fullPath = Path.GetFullPath(scope.FilePath);
            var doc = project.Documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            return doc is null ? [] : [doc];
        }
        return project.Documents.Where(d =>
            !string.IsNullOrEmpty(d.FilePath) &&
            string.Equals(Path.GetExtension(d.FilePath), ".cs", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rewrites every top-level pattern match in a syntax tree. A "match" is any descendant
    /// whose raw kind matches the pattern AND whose structure matches the pattern under
    /// placeholder substitution. Matching nodes are replaced with the goal node with captured
    /// placeholders substituted.
    /// </summary>
    private sealed class StructuralRewriter : CSharpSyntaxRewriter
    {
        private readonly SyntaxNode _pattern;
        private readonly SyntaxNode _goal;
        private readonly IReadOnlyCollection<string> _placeholderNames;
        public int MatchCount { get; private set; }

        public StructuralRewriter(SyntaxNode pattern, SyntaxNode goal, IReadOnlyCollection<string> placeholderNames)
        {
            _pattern = pattern;
            _goal = goal;
            _placeholderNames = placeholderNames;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node is null) return null;

            // Skip replacement when the node is the pattern or goal themselves (shouldn't happen
            // across documents, but guards against re-entrant trees).
            var captures = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
            if (TryMatch(_pattern, node, captures))
            {
                MatchCount++;
                var substituted = Substitute(_goal, captures);
                return substituted.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
            }

            return base.Visit(node);
        }

        private bool TryMatch(SyntaxNode patternNode, SyntaxNode candidate, Dictionary<string, SyntaxNode> captures)
        {
            // Placeholder: any leaf identifier __name__ captures whatever candidate is.
            if (patternNode is IdentifierNameSyntax id)
            {
                var text = id.Identifier.ValueText;
                var match = PlaceholderPattern.Match(text);
                if (match.Success && match.Length == text.Length)
                {
                    var name = match.Groups["name"].Value;
                    if (captures.TryGetValue(name, out var existing))
                    {
                        return SyntaxFactory.AreEquivalent(existing, candidate, topLevel: false);
                    }
                    captures[name] = candidate;
                    return true;
                }
            }

            if (patternNode.RawKind != candidate.RawKind) return false;

            var patternChildren = patternNode.ChildNodesAndTokens().ToArray();
            var candidateChildren = candidate.ChildNodesAndTokens().ToArray();
            if (patternChildren.Length != candidateChildren.Length) return false;

            for (var i = 0; i < patternChildren.Length; i++)
            {
                var pc = patternChildren[i];
                var cc = candidateChildren[i];
                if (pc.IsNode && cc.IsNode)
                {
                    if (!TryMatch(pc.AsNode()!, cc.AsNode()!, captures)) return false;
                }
                else if (pc.IsToken && cc.IsToken)
                {
                    if (pc.RawKind != cc.RawKind) return false;
                    var pt = pc.AsToken();
                    var ct2 = cc.AsToken();
                    // Token-level placeholders: an identifier token whose text matches
                    // __foo__ acts like a placeholder too (covers identifier-shape patterns
                    // like __name__.Method()).
                    var tokenText = pt.ValueText;
                    var placeholderMatch = PlaceholderPattern.Match(tokenText);
                    if (placeholderMatch.Success && placeholderMatch.Length == tokenText.Length)
                    {
                        // Represent a captured token as an identifier-name syntax so substitution
                        // can splice it back in uniformly.
                        var name = placeholderMatch.Groups["name"].Value;
                        var newIdent = SyntaxFactory.IdentifierName(ct2.ValueText);
                        if (captures.TryGetValue(name, out var existing))
                        {
                            if (!SyntaxFactory.AreEquivalent(existing, newIdent, topLevel: false)) return false;
                        }
                        else
                        {
                            captures[name] = newIdent;
                        }
                        continue;
                    }
                    if (!string.Equals(pt.ValueText, ct2.ValueText, StringComparison.Ordinal)) return false;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        private SyntaxNode Substitute(SyntaxNode goal, IReadOnlyDictionary<string, SyntaxNode> captures)
        {
            if (_placeholderNames.Count == 0) return goal;

            return new PlaceholderSubstituter(captures).Visit(goal) ?? goal;
        }

        private sealed class PlaceholderSubstituter : CSharpSyntaxRewriter
        {
            private readonly IReadOnlyDictionary<string, SyntaxNode> _captures;
            public PlaceholderSubstituter(IReadOnlyDictionary<string, SyntaxNode> captures) { _captures = captures; }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                var text = node.Identifier.ValueText;
                var match = PlaceholderPattern.Match(text);
                if (match.Success && match.Length == text.Length)
                {
                    if (_captures.TryGetValue(match.Groups["name"].Value, out var captured))
                    {
                        return captured.WithTriviaFrom(node);
                    }
                }
                return base.VisitIdentifierName(node);
            }
        }
    }
}
