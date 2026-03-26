using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CodeMetricsService : ICodeMetricsService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CodeMetricsService> _logger;

    public CodeMetricsService(IWorkspaceManager workspace, ILogger<CodeMetricsService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ComplexityMetricsDto>> GetComplexityMetricsAsync(
        string workspaceId, string? filePath, string? projectFilter, int? minComplexity, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<ComplexityMetricsDto>();

        IEnumerable<Document> documents;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalizedPath = Path.GetFullPath(filePath);
            documents = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath is not null && Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);
            documents = projects.SelectMany(p => p.Documents);
        }

        foreach (var doc in documents)
        {
            if (ct.IsCancellationRequested || results.Count >= limit) break;

            var tree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (tree is null || semanticModel is null) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            var methodDeclarations = root.DescendantNodes()
                .Where(n => n is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);

            foreach (var decl in methodDeclarations)
            {
                if (ct.IsCancellationRequested || results.Count >= limit) break;

                var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                if (symbol is null) continue;

                var complexity = CalculateCyclomaticComplexity(decl);
                if (minComplexity.HasValue && complexity < minComplexity.Value) continue;

                var loc = CalculateLinesOfCode(decl);
                var nesting = CalculateMaxNestingDepth(decl);
                var paramCount = symbol is IMethodSymbol m ? m.Parameters.Length : 0;

                var lineSpan = decl.GetLocation().GetLineSpan();

                results.Add(new ComplexityMetricsDto(
                    symbol.Name,
                    symbol.Kind.ToString(),
                    lineSpan.Path,
                    lineSpan.StartLinePosition.Line + 1,
                    complexity,
                    loc,
                    nesting,
                    paramCount,
                    symbol.ContainingType?.Name));
            }
        }

        return results.OrderByDescending(r => r.CyclomaticComplexity).Take(limit).ToList();
    }

    private static int CalculateCyclomaticComplexity(SyntaxNode node)
    {
        var complexity = 1; // Base complexity
        foreach (var child in node.DescendantNodes())
        {
            complexity += child switch
            {
                IfStatementSyntax => 1,
                ElseClauseSyntax { Statement: not IfStatementSyntax } => 0, // else-if counted by if
                CaseSwitchLabelSyntax => 1,
                CasePatternSwitchLabelSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                ConditionalExpressionSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                DoStatementSyntax => 1,
                CatchClauseSyntax => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0
            };
        }
        return complexity;
    }

    private static int CalculateLinesOfCode(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static int CalculateMaxNestingDepth(SyntaxNode node)
    {
        var maxDepth = 0;
        CalculateNestingDepthRecursive(node, 0, ref maxDepth);
        return maxDepth;
    }

    private static void CalculateNestingDepthRecursive(SyntaxNode node, int currentDepth, ref int maxDepth)
    {
        foreach (var child in node.ChildNodes())
        {
            var newDepth = child switch
            {
                BlockSyntax when child.Parent is IfStatementSyntax or ElseClauseSyntax
                    or WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                    or DoStatementSyntax or TryStatementSyntax or CatchClauseSyntax
                    or LockStatementSyntax or UsingStatementSyntax => currentDepth + 1,
                _ => currentDepth
            };

            if (newDepth > maxDepth) maxDepth = newDepth;
            CalculateNestingDepthRecursive(child, newDepth, ref maxDepth);
        }
    }
}
