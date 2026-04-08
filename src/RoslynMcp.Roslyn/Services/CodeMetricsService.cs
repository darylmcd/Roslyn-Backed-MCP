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
            if (ct.IsCancellationRequested) break;

            var tree = await doc.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            var semanticModel = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (tree is null || semanticModel is null) continue;

            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
            var methodDeclarations = root.DescendantNodes()
                .Where(n => n is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);

            foreach (var decl in methodDeclarations)
            {
                if (ct.IsCancellationRequested) break;

                var symbol = semanticModel.GetDeclaredSymbol(decl, ct);
                if (symbol is null) continue;

                var complexity = CalculateCyclomaticComplexity(decl);
                if (minComplexity.HasValue && complexity < minComplexity.Value) continue;

                var loc = CalculateLinesOfCode(decl);
                var nesting = CalculateMaxNestingDepthForMember(decl);
                var paramCount = symbol is IMethodSymbol m ? m.Parameters.Length : 0;

                var lineSpan = decl.GetLocation().GetLineSpan();
                var maintainability = ComputeMaintainabilityIndex(complexity, loc);

                results.Add(new ComplexityMetricsDto(
                    symbol.Name,
                    symbol.Kind.ToString(),
                    lineSpan.Path,
                    lineSpan.StartLinePosition.Line + 1,
                    complexity,
                    loc,
                    nesting,
                    paramCount,
                    symbol.ContainingType?.Name,
                    maintainability));
            }
        }

        return results.OrderByDescending(r => r.CyclomaticComplexity).Take(limit).ToList();
    }

    /// <summary>
    /// Approximate Visual Studio-style maintainability index (0–100, higher is better).
    /// Uses cyclomatic complexity and LOC with a Halstead-volume heuristic when full Halstead metrics are not computed.
    /// </summary>
    private static double ComputeMaintainabilityIndex(int cyclomaticComplexity, int linesOfCode)
    {
        var loc = Math.Max(1, linesOfCode);
        var cc = Math.Max(1, cyclomaticComplexity);
        var hv = Math.Max(1.0, loc * Math.Log(1 + cc, 2));
        var raw = 171.0 - 5.2 * Math.Log(hv) - 0.23 * cc - 16.2 * Math.Log(loc);
        var index = raw * 100.0 / 171.0;
        return Math.Round(Math.Clamp(index, 0, 100), 2);
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

    /// <summary>
    /// Maximum control-flow nesting depth inside a method, constructor, or property accessor body.
    /// Counts single-statement bodies (e.g. <c>if (x) Foo();</c>) and expression-bodied members, not only braced blocks.
    /// </summary>
    private static int CalculateMaxNestingDepthForMember(SyntaxNode decl)
    {
        var maxDepth = 0;
        switch (decl)
        {
            case MethodDeclarationSyntax m:
                if (m.Body is not null)
                    VisitControlFlowNesting(m.Body, 0, ref maxDepth);
                else if (m.ExpressionBody is not null)
                    VisitControlFlowNesting(m.ExpressionBody.Expression, 0, ref maxDepth);
                break;
            case ConstructorDeclarationSyntax c:
                if (c.Body is not null)
                    VisitControlFlowNesting(c.Body, 0, ref maxDepth);
                break;
            case PropertyDeclarationSyntax p:
                if (p.ExpressionBody is not null)
                    VisitControlFlowNesting(p.ExpressionBody.Expression, 0, ref maxDepth);
                else if (p.AccessorList is not null)
                {
                    foreach (var accessor in p.AccessorList.Accessors)
                    {
                        if (accessor.Body is not null)
                            VisitControlFlowNesting(accessor.Body, 0, ref maxDepth);
                        else if (accessor.ExpressionBody is not null)
                            VisitControlFlowNesting(accessor.ExpressionBody.Expression, 0, ref maxDepth);
                    }
                }

                break;
        }

        return maxDepth;
    }

    private static void VisitControlFlowNesting(SyntaxNode node, int depth, ref int maxDepth)
    {
        if (depth > maxDepth)
        {
            maxDepth = depth;
        }

        foreach (var child in node.ChildNodes())
        {
            VisitChildForNesting(child, depth, ref maxDepth);
        }
    }

    private static void VisitChildForNesting(SyntaxNode child, int depth, ref int maxDepth)
    {
        switch (child)
        {
            case IfStatementSyntax ifs:
                VisitControlFlowNesting(ifs.Condition, depth, ref maxDepth);
                VisitControlFlowNesting(ifs.Statement, depth + 1, ref maxDepth);
                if (ifs.Else is not null)
                    VisitControlFlowNesting(ifs.Else.Statement, depth + 1, ref maxDepth);
                break;

            case ForStatementSyntax fs:
                VisitForLoop(fs, depth, ref maxDepth);
                break;

            case SwitchStatementSyntax sw:
                foreach (var section in sw.Sections)
                    foreach (var stmt in section.Statements)
                        VisitControlFlowNesting(stmt, depth + 1, ref maxDepth);
                break;

            case TryStatementSyntax tr:
                VisitTryStatement(tr, depth, ref maxDepth);
                break;

            case SwitchExpressionSyntax se:
                foreach (var arm in se.Arms)
                    VisitControlFlowNesting(arm.Expression, depth + 1, ref maxDepth);
                break;

            case LocalFunctionStatementSyntax lf:
                if (lf.Body is not null)
                    VisitControlFlowNesting(lf.Body, depth, ref maxDepth);
                else if (lf.ExpressionBody is not null)
                    VisitControlFlowNesting(lf.ExpressionBody.Expression, depth, ref maxDepth);
                break;

            // Statements that nest a single body at depth+1.
            case WhileStatementSyntax ws:
                VisitControlFlowNesting(ws.Statement, depth + 1, ref maxDepth);
                break;
            case DoStatementSyntax ds:
                VisitControlFlowNesting(ds.Statement, depth + 1, ref maxDepth);
                break;
            case ForEachStatementSyntax fes:
                VisitControlFlowNesting(fes.Statement, depth + 1, ref maxDepth);
                break;
            case LockStatementSyntax lk:
                VisitControlFlowNesting(lk.Statement, depth + 1, ref maxDepth);
                break;
            case UsingStatementSyntax us:
                VisitControlFlowNesting(us.Statement, depth + 1, ref maxDepth);
                break;
            case FixedStatementSyntax fx:
                VisitControlFlowNesting(fx.Statement, depth + 1, ref maxDepth);
                break;
            case CheckedStatementSyntax chk:
                VisitControlFlowNesting(chk.Block, depth + 1, ref maxDepth);
                break;
            case UnsafeStatementSyntax uns:
                VisitControlFlowNesting(uns.Block, depth + 1, ref maxDepth);
                break;

            default:
                VisitControlFlowNesting(child, depth, ref maxDepth);
                break;
        }
    }

    private static void VisitForLoop(ForStatementSyntax fs, int depth, ref int maxDepth)
    {
        if (fs.Declaration is not null)
            VisitControlFlowNesting(fs.Declaration, depth, ref maxDepth);

        foreach (var init in fs.Initializers)
            VisitControlFlowNesting(init, depth, ref maxDepth);

        if (fs.Condition is not null)
            VisitControlFlowNesting(fs.Condition, depth, ref maxDepth);

        VisitControlFlowNesting(fs.Statement, depth + 1, ref maxDepth);
    }

    private static void VisitTryStatement(TryStatementSyntax tr, int depth, ref int maxDepth)
    {
        VisitControlFlowNesting(tr.Block, depth + 1, ref maxDepth);

        foreach (var c in tr.Catches)
            VisitControlFlowNesting(c.Block, depth + 1, ref maxDepth);

        if (tr.Finally is not null)
            VisitControlFlowNesting(tr.Finally.Block, depth + 1, ref maxDepth);
    }
}
