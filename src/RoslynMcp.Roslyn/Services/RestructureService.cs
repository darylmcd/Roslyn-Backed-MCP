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
    // Examples: __foo__ and __bar_42__. Matching requires both trailing underscores.
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
        var (patternNode, goalNode, patternKind, goalPlaceholderNames) = ValidateAndParseArguments(pattern, goal);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var accumulator = solution;
        var changes = new List<FileChangeDto>();
        var totalMatches = 0;

        foreach (var project in SolutionScopeHelper.EnumerateProjects(solution, scope))
        {
            foreach (var document in SolutionScopeHelper.EnumerateDocuments(project, scope))
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var rewriter = new StructuralRewriter(patternNode, goalNode, goalPlaceholderNames);
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

    /// <summary>
    /// symbol-refactor-preview-auto-applies-without-explicit-apply-call: pure-functional
    /// structural-rewrite simulation that operates on an explicit input
    /// <paramref name="inputSolution"/> and returns the post-rewrite <see cref="Solution"/>
    /// snapshot. Mirrors <see cref="PreviewRestructureAsync"/>'s parse-and-walk logic but
    /// never touches the workspace, the disk, or <see cref="IPreviewStore"/>. Used by
    /// <see cref="SymbolRefactorService.PreviewAsync"/> to chain ops in-memory so each op
    /// sees its predecessor's rewrites without the previous auto-apply-each-step disk write
    /// that fired before the agent ever called <c>apply_composite_preview</c>.
    /// </summary>
    internal async Task<(Solution NewSolution, IReadOnlyList<FileChangeDto> Changes, string Description)>
        PreviewRestructureOnSolutionAsync(
            Solution inputSolution, string pattern, string goal, RestructureScope scope, CancellationToken ct)
    {
        var (patternNode, goalNode, patternKind, goalPlaceholderNames) = ValidateAndParseArguments(pattern, goal);

        var accumulator = inputSolution;
        var changes = new List<FileChangeDto>();
        var totalMatches = 0;

        foreach (var project in SolutionScopeHelper.EnumerateProjects(inputSolution, scope))
        {
            foreach (var document in SolutionScopeHelper.EnumerateDocuments(project, scope))
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var rewriter = new StructuralRewriter(patternNode, goalNode, goalPlaceholderNames);
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
        return (accumulator, changes, description);
    }

    private static (SyntaxNode PatternNode, SyntaxNode GoalNode, string PatternKind,
        IReadOnlyCollection<string> GoalPlaceholderNames) ValidateAndParseArguments(string pattern, string goal)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new PublicArgumentException("pattern must be non-empty.", nameof(pattern));
        if (goal is null)
            throw new PublicArgumentException(
                "goal must be non-null. Supply a valid C# expression or statement.", nameof(goal));

        var (patternNode, patternKind) = ParsePatternOrGoal(pattern, nameof(pattern));
        var (goalNode, goalKind) = ParsePatternOrGoal(goal, nameof(goal));
        if (patternKind != goalKind)
        {
            throw new PublicArgumentException(
                "pattern and goal must be the same syntactic kind: both expressions or both statements.",
                nameof(goal));
        }

        var patternPlaceholderNames = ExtractPlaceholderNames(patternNode);
        var goalPlaceholderNames = ExtractPlaceholderNames(goalNode);
        if (goalPlaceholderNames.Except(patternPlaceholderNames, StringComparer.Ordinal).Any())
        {
            throw new PublicArgumentException(
                "goal references placeholders not captured by the pattern. " +
                "Every capture placeholder in the goal must appear in the pattern so it has a value to substitute.",
                nameof(goal));
        }

        return (patternNode, goalNode, patternKind, goalPlaceholderNames);
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

        throw new PublicArgumentException(
            $"Parameter '{argName}' could not be parsed as a C# expression or statement. Fix the syntax and retry.",
            argName);
    }

    private static IReadOnlyCollection<string> ExtractPlaceholderNames(SyntaxNode pattern)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in pattern.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (TryGetPlaceholderName(identifier.Identifier.ValueText, out var name))
            {
                names.Add(name);
            }
        }
        return names;
    }

    private static bool TryGetPlaceholderName(string text, out string name)
    {
        var match = PlaceholderPattern.Match(text);
        if (match.Success && match.Length == text.Length)
        {
            name = match.Groups["name"].Value;
            return true;
        }

        name = string.Empty;
        return false;
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
        private readonly IReadOnlyCollection<string> _goalPlaceholderNames;
        public int MatchCount { get; private set; }

        public StructuralRewriter(
            SyntaxNode pattern,
            SyntaxNode goal,
            IReadOnlyCollection<string> goalPlaceholderNames)
        {
            _pattern = pattern;
            _goal = goal;
            _goalPlaceholderNames = goalPlaceholderNames;
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
                // restructure-preview-splice-without-parenthesization: the substituted goal lands
                // in the matched node's slot, which may bind tighter than the goal does.
                var substituted = ParenthesizeForSlot(Substitute(_goal, captures), node);
                return substituted.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
            }

            return base.Visit(node);
        }

        private bool TryMatch(SyntaxNode patternNode, SyntaxNode candidate, Dictionary<string, SyntaxNode> captures)
        {
            // Placeholder: any leaf identifier __name__ captures whatever candidate is.
            if (TryCaptureIdentifierPlaceholder(patternNode, candidate, captures, out var placeholderMatched))
            {
                return placeholderMatched;
            }

            if (patternNode.RawKind != candidate.RawKind) return false;

            return TryMatchChildren(patternNode, candidate, captures);
        }

        private bool TryMatchChildren(SyntaxNode patternNode, SyntaxNode candidate, Dictionary<string, SyntaxNode> captures)
        {
            var patternChildren = patternNode.ChildNodesAndTokens().ToArray();
            var candidateChildren = candidate.ChildNodesAndTokens().ToArray();
            if (patternChildren.Length != candidateChildren.Length) return false;

            for (var i = 0; i < patternChildren.Length; i++)
            {
                if (!TryMatchChild(patternChildren[i], candidateChildren[i], captures))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryMatchChild(
            SyntaxNodeOrToken patternChild,
            SyntaxNodeOrToken candidateChild,
            Dictionary<string, SyntaxNode> captures)
        {
            if (patternChild.IsNode && candidateChild.IsNode)
            {
                return TryMatch(patternChild.AsNode()!, candidateChild.AsNode()!, captures);
            }

            if (patternChild.IsToken && candidateChild.IsToken)
            {
                return TryMatchToken(patternChild.AsToken(), candidateChild.AsToken(), captures);
            }

            return false;
        }

        private static bool TryCaptureIdentifierPlaceholder(
            SyntaxNode patternNode,
            SyntaxNode candidate,
            Dictionary<string, SyntaxNode> captures,
            out bool placeholderMatched)
        {
            placeholderMatched = false;
            if (patternNode is not IdentifierNameSyntax identifier ||
                !TryGetPlaceholderName(identifier.Identifier.ValueText, out var name))
            {
                return false;
            }

            placeholderMatched = TryCapturePlaceholder(name, candidate, captures);
            return true;
        }

        private static bool TryMatchToken(
            SyntaxToken patternToken,
            SyntaxToken candidateToken,
            Dictionary<string, SyntaxNode> captures)
        {
            if (patternToken.RawKind != candidateToken.RawKind)
            {
                return false;
            }

            // Token-level placeholders: an identifier token whose text matches __foo__
            // acts like a placeholder too (covers identifier-shape patterns like
            // __name__.Method()).
            if (TryGetPlaceholderName(patternToken.ValueText, out var name))
            {
                var capturedToken = SyntaxFactory.IdentifierName(candidateToken.ValueText);
                return TryCapturePlaceholder(name, capturedToken, captures);
            }

            return string.Equals(patternToken.ValueText, candidateToken.ValueText, StringComparison.Ordinal);
        }

        private static bool TryCapturePlaceholder(
            string name,
            SyntaxNode candidate,
            Dictionary<string, SyntaxNode> captures)
        {
            if (captures.TryGetValue(name, out var existing))
            {
                return SyntaxFactory.AreEquivalent(existing, candidate, topLevel: false);
            }

            captures[name] = candidate;
            return true;
        }

        private SyntaxNode Substitute(SyntaxNode goal, IReadOnlyDictionary<string, SyntaxNode> captures)
        {
            // dr-9-1-regression-r17a-emits-literal-placeholder: substitution now gates on the
            // goal's placeholder set, not the pattern's. A goal with no placeholders is returned
            // unchanged (fast path); otherwise every `__name__` in the goal gets rewritten from
            // the captures dict. Pre-fix the gate used the pattern's set, so any pattern without
            // placeholders returned the goal verbatim — literal `__name__` text included.
            if (_goalPlaceholderNames.Count == 0) return goal;

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
                        // restructure-preview-splice-without-parenthesization: pre-fix the capture
                        // was spliced verbatim, so `__a__ + 1` -> `__a__ * 3` over `a + b + 1`
                        // emitted `a + b * 3`. The placeholder is a primary expression, so any
                        // looser capture needs parentheses when its slot is an operator operand.
                        return ParenthesizeForSlot(captured, node).WithTriviaFrom(node);
                    }
                }
                return base.VisitIdentifierName(node);
            }
        }

        /// <summary>
        /// Wraps <paramref name="replacement"/> in parentheses when it is an expression that binds
        /// looser than a primary expression AND <paramref name="slot"/> (the original node it
        /// replaces, still attached to its parent) is an operand position of an operator. Slots
        /// that accept any expression (arguments, initializers, statements, assignment right-hand
        /// sides) and non-expression positions (types, names) are left untouched, so no redundant
        /// parentheses appear there. Conservative by design: an operand slot always gets
        /// parentheses for a non-primary replacement, even when precedence would allow omitting them.
        /// </summary>
        private static SyntaxNode ParenthesizeForSlot(SyntaxNode replacement, SyntaxNode slot)
        {
            if (replacement is not ExpressionSyntax expression || IsPrimaryExpression(expression) || !IsOperandSlot(slot))
            {
                return replacement;
            }

            return SyntaxFactory.ParenthesizedExpression(expression.WithoutTrivia()).WithTriviaFrom(expression);
        }

        private static bool IsPrimaryExpression(ExpressionSyntax expression) => expression switch
        {
            IdentifierNameSyntax or GenericNameSyntax or PredefinedTypeSyntax => true,
            LiteralExpressionSyntax or InterpolatedStringExpressionSyntax => true,
            ParenthesizedExpressionSyntax or TupleExpressionSyntax => true,
            MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax => true,
            ThisExpressionSyntax or BaseExpressionSyntax => true,
            TypeOfExpressionSyntax or DefaultExpressionSyntax or SizeOfExpressionSyntax or CheckedExpressionSyntax => true,
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax => true,
            // x++, x--, x! — postfix operators bind as tightly as primary expressions.
            PostfixUnaryExpressionSyntax => true,
            _ => false,
        };

        private static bool IsOperandSlot(SyntaxNode slot) => slot.Parent switch
        {
            BinaryExpressionSyntax binary => binary.Left == slot ||
                // The right side of `is` / `as` is a type, not an operand.
                !(binary.IsKind(SyntaxKind.IsExpression) || binary.IsKind(SyntaxKind.AsExpression)),
            AssignmentExpressionSyntax assignment => assignment.Left == slot,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AwaitExpressionSyntax => true,
            RangeExpressionSyntax => true,
            CastExpressionSyntax cast => cast.Expression == slot,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == slot,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression == slot,
            InvocationExpressionSyntax invocation => invocation.Expression == slot,
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression == slot,
            ConditionalExpressionSyntax conditional => conditional.Condition == slot,
            IsPatternExpressionSyntax isPattern => isPattern.Expression == slot,
            SwitchExpressionSyntax switchExpression => switchExpression.GoverningExpression == slot,
            WithExpressionSyntax withExpression => withExpression.Expression == slot,
            _ => false,
        };
    }
}

/// <summary>
/// Resolves the project/document set a <see cref="RestructureScope"/> selects. Shared by
/// <see cref="RestructureService"/> and <see cref="StringLiteralReplaceService"/>, which both
/// scope solution-wide sweeps the same way.
/// </summary>
internal static class SolutionScopeHelper
{
    public static IEnumerable<Project> EnumerateProjects(Solution solution, RestructureScope scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.ProjectName))
        {
            var canonicalProjectPath = Path.IsPathFullyQualified(scope.ProjectName)
                ? PhysicalPathResolver.Resolve(scope.ProjectName)
                : null;
            var match = solution.Projects.FirstOrDefault(p =>
                string.Equals(p.Name, scope.ProjectName, StringComparison.OrdinalIgnoreCase) ||
                (canonicalProjectPath is not null && p.FilePath is not null &&
                    FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(p.FilePath), canonicalProjectPath)));
            if (match is null)
                throw new InvalidOperationException($"Project '{scope.ProjectName}' not found in workspace.");
            return [match];
        }
        return solution.Projects;
    }

    public static IEnumerable<Document> EnumerateDocuments(Project project, RestructureScope scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.FilePath))
        {
            var fullPath = PhysicalPathResolver.Resolve(scope.FilePath);
            var doc = project.Documents.FirstOrDefault(d =>
                d.FilePath is not null &&
                FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(d.FilePath), fullPath));
            return doc is null ? [] : [doc];
        }
        return project.Documents.Where(d =>
            !string.IsNullOrEmpty(d.FilePath) &&
            string.Equals(Path.GetExtension(d.FilePath), ".cs", StringComparison.OrdinalIgnoreCase));
    }
}
