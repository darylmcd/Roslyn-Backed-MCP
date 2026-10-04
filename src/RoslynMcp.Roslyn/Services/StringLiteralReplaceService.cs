using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Rewrites <see cref="LiteralExpressionSyntax"/> nodes of kind
/// <see cref="SyntaxKind.StringLiteralExpression"/> whose parent is one of:
/// <list type="bullet">
///   <item><description><see cref="ArgumentSyntax"/> — method call / constructor arg.</description></item>
///   <item><description><see cref="AttributeArgumentSyntax"/> — attribute positional/named arg.</description></item>
///   <item><description><see cref="EqualsValueClauseSyntax"/> — default value / field or local initializer.</description></item>
///   <item><description><see cref="AssignmentExpressionSyntax"/> right side — property or field assignment.</description></item>
///   <item><description><see cref="InitializerExpressionSyntax"/> element — collection initializer.</description></item>
///   <item><description><see cref="ReturnStatementSyntax"/> — returned literal.</description></item>
/// </list>
/// Skips <c>nameof()</c> and interpolated-string holes (they are not <c>LiteralExpressionSyntax</c>
/// in the first place).
/// </summary>
public sealed class StringLiteralReplaceService : IStringLiteralReplaceService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public StringLiteralReplaceService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewReplaceAsync(
        string workspaceId,
        IReadOnlyList<StringLiteralReplacementDto> replacements,
        RestructureScope scope,
        CancellationToken ct)
    {
        if (replacements is null || replacements.Count == 0)
            throw new PublicArgumentException("At least one replacement is required.", nameof(replacements));

        var byLiteral = new Dictionary<string, StringLiteralReplacementDto>(StringComparer.Ordinal);
        foreach (var r in replacements)
        {
            if (string.IsNullOrEmpty(r.LiteralValue))
                throw new PublicArgumentException("replacement.literalValue must be non-empty.", nameof(replacements));
            if (string.IsNullOrWhiteSpace(r.ReplacementExpression))
                throw new PublicArgumentException("replacement.replacementExpression must be non-empty.", nameof(replacements));
            byLiteral[r.LiteralValue] = r;
        }

        var expressions = byLiteral.ToDictionary(
            pair => pair.Key,
            pair => SyntaxFactory.ParseExpression(pair.Value.ReplacementExpression),
            StringComparer.Ordinal);

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var accumulator = solution;
        var changes = new List<FileChangeDto>();
        var totalHits = 0;

        foreach (var project in SolutionScopeHelper.EnumerateProjects(solution, scope))
        {
            foreach (var document in SolutionScopeHelper.EnumerateDocuments(project, scope))
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is not CompilationUnitSyntax compilationUnit) continue;

                // Bind symbol-bearing expressions in their actual insertion context. A
                // constant can be declared in another document, so syntax names alone
                // cannot identify its own initializer.
                var probe = new LiteralRewriter(byLiteral, expressions);
                var probeRoot = (CompilationUnitSyntax)probe.Visit(compilationUnit)!;
                if (probe.HitCount == 0) continue;
                var excludedPositions = new HashSet<int>();
                if (probe.Pending.Count > 0)
                {
                    probeRoot = AddRequiredUsings(probeRoot, probe.RequiredUsings);
                    var probeDocument = document.WithSyntaxRoot(probeRoot);
                    var boundRoot = await probeDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Replacement syntax could not be loaded.");
                    var semanticModel = await probeDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Replacement expression could not be bound.");
                    foreach (var pending in probe.Pending)
                    {
                        var expression = boundRoot.GetAnnotatedNodes(pending.Annotation)
                            .OfType<ExpressionSyntax>().Single();
                        var declarator = expression.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                        var declaringSymbol = declarator is null ? null : semanticModel.GetDeclaredSymbol(declarator, ct);
                        var isOwnConstInitializer = false;
                        foreach (var name in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                        {
                            var symbol = semanticModel.GetSymbolInfo(name, ct).Symbol;
                            if (symbol is null || symbol is IErrorTypeSymbol)
                            {
                                throw new InvalidOperationException(
                                    $"Replacement expression '{pending.ReplacementExpression}' could not be bound in {document.Name}.");
                            }
                            if ((declaringSymbol is IFieldSymbol { IsConst: true } ||
                                 declaringSymbol is ILocalSymbol { IsConst: true }) &&
                                SymbolEqualityComparer.Default.Equals(symbol, declaringSymbol))
                            {
                                isOwnConstInitializer = true;
                            }
                        }
                        if (isOwnConstInitializer)
                            excludedPositions.Add(pending.OriginalPosition);
                    }
                }

                var rewriter = new LiteralRewriter(byLiteral, expressions, excludedPositions);
                var newRoot = (CompilationUnitSyntax)rewriter.Visit(compilationUnit)!;
                if (rewriter.HitCount == 0) continue;
                totalHits += rewriter.HitCount;
                newRoot = AddRequiredUsings(newRoot, rewriter.RequiredUsings);

                var oldText = compilationUnit.ToFullString();
                var newText = newRoot.ToFullString();
                accumulator = accumulator.WithDocumentText(document.Id, SourceText.From(newText));
                var docPath = document.FilePath ?? document.Name;
                changes.Add(new FileChangeDto(
                    FilePath: docPath,
                    UnifiedDiff: DiffGenerator.GenerateUnifiedDiff(oldText, newText, docPath)));
            }
        }

        if (changes.Count == 0)
        {
            // Structured empty-preview response rather than an exception. Zero matches is a
            // valid outcome for a find-style preview (e.g. the caller is probing whether a
            // literal exists in scope); throwing forced callers to `try/catch` on a
            // semantically benign case. Mirrors the `FixAllService` empty-preview shape —
            // empty token + empty changes + descriptive `Description`.
            var emptyDescription = "No matching string literals found in scope.";
            return new RefactoringPreviewDto(
                PreviewToken: string.Empty,
                Description: emptyDescription,
                Changes: Array.Empty<FileChangeDto>(),
                Warnings: null);
        }

        var description = $"Replace {totalHits} string literal(s) across {changes.Count} file(s)";
        var token = _previewStore.Store(workspaceId, accumulator, _workspace.GetCurrentVersion(workspaceId), description);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static bool HasUsing(CompilationUnitSyntax root, string ns)
    {
        foreach (var u in root.Usings)
        {
            if (u.Name is not null && string.Equals(u.Name.ToString(), ns, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static CompilationUnitSyntax AddRequiredUsings(CompilationUnitSyntax root, IEnumerable<string> namespaces)
    {
        foreach (var ns in namespaces)
        {
            if (!HasUsing(root, ns))
            {
                var usingDir = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                    .NormalizeWhitespace()
                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                root = root.AddUsings(usingDir);
            }
        }
        return root;
    }

    private sealed class LiteralRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<string, StringLiteralReplacementDto> _byLiteral;
        private readonly IReadOnlyDictionary<string, ExpressionSyntax> _expressions;
        private readonly IReadOnlySet<int> _excludedPositions;
        public int HitCount { get; private set; }
        public HashSet<string> RequiredUsings { get; } = new(StringComparer.Ordinal);
        public List<PendingReplacement> Pending { get; } = [];

        public LiteralRewriter(
            IReadOnlyDictionary<string, StringLiteralReplacementDto> byLiteral,
            IReadOnlyDictionary<string, ExpressionSyntax> expressions,
            IReadOnlySet<int>? excludedPositions = null)
        {
            _byLiteral = byLiteral;
            _expressions = expressions;
            _excludedPositions = excludedPositions ?? new HashSet<int>();
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (!node.IsKind(SyntaxKind.StringLiteralExpression))
                return base.VisitLiteralExpression(node);

            // Only replace when the literal is in an argument/initializer position so we don't
            // touch log templates, exception messages in throw-expressions, or XML attribute
            // values.
            if (!IsAllowedPosition(node.Parent))
                return base.VisitLiteralExpression(node);

            var value = node.Token.ValueText;
            if (!_byLiteral.TryGetValue(value, out var replacement) || _excludedPositions.Contains(node.SpanStart))
                return base.VisitLiteralExpression(node);

            HitCount++;
            if (!string.IsNullOrWhiteSpace(replacement.UsingNamespace))
                RequiredUsings.Add(replacement.UsingNamespace!);

            var annotation = new SyntaxAnnotation();
            var expr = _expressions[value].WithTriviaFrom(node).WithAdditionalAnnotations(annotation);
            if (expr.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any())
                Pending.Add(new PendingReplacement(annotation, node.SpanStart, replacement.ReplacementExpression));
            return expr;
        }

        private static bool IsAllowedPosition(SyntaxNode? parent) => parent switch
        {
            ArgumentSyntax => true,
            AttributeArgumentSyntax => true,
            EqualsValueClauseSyntax => true,
            AssignmentExpressionSyntax assign when assign.Right is LiteralExpressionSyntax => true,
            InitializerExpressionSyntax => true,
            ReturnStatementSyntax => true,
            _ => false,
        };
    }

    private sealed record PendingReplacement(SyntaxAnnotation Annotation, int OriginalPosition, string ReplacementExpression);
}
