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
/// Item 7 implementation. Rewrites <see cref="LiteralExpressionSyntax"/> nodes of kind
/// <see cref="SyntaxKind.StringLiteralExpression"/> whose parent is one of:
/// <list type="bullet">
///   <item><description><see cref="ArgumentSyntax"/> — method call / constructor arg.</description></item>
///   <item><description><see cref="AttributeArgumentSyntax"/> — attribute positional/named arg.</description></item>
///   <item><description><see cref="EqualsValueClauseSyntax"/> — default value / field initializer.</description></item>
///   <item><description><see cref="AssignmentExpressionSyntax"/> right side — property or field assignment.</description></item>
///   <item><description><see cref="ArrayInitializerExpressionSyntax"/> element — collection initializer.</description></item>
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
            throw new ArgumentException("At least one replacement is required.", nameof(replacements));

        var byLiteral = new Dictionary<string, StringLiteralReplacementDto>(StringComparer.Ordinal);
        foreach (var r in replacements)
        {
            if (string.IsNullOrEmpty(r.LiteralValue))
                throw new ArgumentException("replacement.literalValue must be non-empty.", nameof(replacements));
            if (string.IsNullOrWhiteSpace(r.ReplacementExpression))
                throw new ArgumentException("replacement.replacementExpression must be non-empty.", nameof(replacements));
            byLiteral[r.LiteralValue] = r;
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var accumulator = solution;
        var changes = new List<FileChangeDto>();
        var totalHits = 0;

        foreach (var project in EnumerateProjects(solution, scope))
        {
            foreach (var document in EnumerateDocuments(project, scope))
            {
                ct.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is not CompilationUnitSyntax compilationUnit) continue;

                var rewriter = new LiteralRewriter(byLiteral);
                var newRoot = (CompilationUnitSyntax)rewriter.Visit(compilationUnit)!;
                if (rewriter.HitCount == 0) continue;
                totalHits += rewriter.HitCount;

                // Inject required using directives from matched replacements.
                foreach (var ns in rewriter.RequiredUsings)
                {
                    if (!HasUsing(newRoot, ns))
                    {
                        var usingDir = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
                        newRoot = newRoot.AddUsings(usingDir);
                    }
                }

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
            throw new InvalidOperationException(
                "replace_string_literals_preview: no matching literals found in scope.");
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

    private sealed class LiteralRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyDictionary<string, StringLiteralReplacementDto> _byLiteral;
        public int HitCount { get; private set; }
        public HashSet<string> RequiredUsings { get; } = new(StringComparer.Ordinal);

        public LiteralRewriter(IReadOnlyDictionary<string, StringLiteralReplacementDto> byLiteral)
        {
            _byLiteral = byLiteral;
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
            if (!_byLiteral.TryGetValue(value, out var replacement))
                return base.VisitLiteralExpression(node);

            HitCount++;
            if (!string.IsNullOrWhiteSpace(replacement.UsingNamespace))
                RequiredUsings.Add(replacement.UsingNamespace!);

            var expr = SyntaxFactory.ParseExpression(replacement.ReplacementExpression).WithTriviaFrom(node);
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
}
