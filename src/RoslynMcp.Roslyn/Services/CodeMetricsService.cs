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
        string workspaceId, string? filePath, IReadOnlyList<string>? filePaths, string? projectFilter, int? minComplexity, int limit, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = new List<ComplexityMetricsDto>();

        // roslyn-mcp-complexity-subset-rerun: build a set of normalized target paths from both
        // the singular `filePath` legacy parameter and the newer `filePaths` list; union (OR)
        // them. Empty list is treated as "no filter" (same as null).
        var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            targetPaths.Add(Path.GetFullPath(filePath));
        }
        if (filePaths is not null)
        {
            foreach (var p in filePaths)
            {
                if (!string.IsNullOrWhiteSpace(p))
                    targetPaths.Add(Path.GetFullPath(p));
            }
        }

        IEnumerable<Document> documents;
        if (targetPaths.Count > 0)
        {
            documents = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath is not null && targetPaths.Contains(Path.GetFullPath(d.FilePath)));
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

    // WS2 session 2.9: dispatcher was cc 21 with 15 inline switch arms covering every
    // control-flow statement kind. Split into per-shape helpers so the outer method
    // reads as a small, obvious kind→helper table instead of a 68-line wall. Helpers
    // are grouped by structural shape (single-body-at-depth+1, single-block-at-depth+1,
    // and the special-cased kinds If/Switch/SwitchExpression/Try/LocalFunction/For).
    // Recursion semantics unchanged — every depth increment and recursion call is
    // byte-identical to the pre-refactor dispatcher.
    private static void VisitChildForNesting(SyntaxNode child, int depth, ref int maxDepth)
    {
        switch (child)
        {
            case IfStatementSyntax ifs:
                VisitIfStatement(ifs, depth, ref maxDepth);
                return;
            case ForStatementSyntax fs:
                VisitForLoop(fs, depth, ref maxDepth);
                return;
            case SwitchStatementSyntax sw:
                VisitSwitchStatement(sw, depth, ref maxDepth);
                return;
            case TryStatementSyntax tr:
                VisitTryStatement(tr, depth, ref maxDepth);
                return;
            case SwitchExpressionSyntax se:
                VisitSwitchExpression(se, depth, ref maxDepth);
                return;
            case LocalFunctionStatementSyntax lf:
                VisitLocalFunction(lf, depth, ref maxDepth);
                return;
        }

        // Statements that nest a single body at depth+1 (while/do/foreach/lock/using/fixed)
        // or a single block at depth+1 (checked/unsafe) are structurally uniform — handle
        // them in two small helpers instead of eight inline switch arms.
        if (TryVisitSingleBodyAtDepthPlusOne(child, depth, ref maxDepth))
            return;
        if (TryVisitSingleBlockAtDepthPlusOne(child, depth, ref maxDepth))
            return;

        // Default fallthrough: not a control-flow statement — recurse into its children
        // at the same depth so nested control flow further down is still discovered.
        VisitControlFlowNesting(child, depth, ref maxDepth);
    }

    private static void VisitIfStatement(IfStatementSyntax ifs, int depth, ref int maxDepth)
    {
        VisitControlFlowNesting(ifs.Condition, depth, ref maxDepth);
        VisitControlFlowNesting(ifs.Statement, depth + 1, ref maxDepth);
        if (ifs.Else is not null)
            VisitControlFlowNesting(ifs.Else.Statement, depth + 1, ref maxDepth);
    }

    private static void VisitSwitchStatement(SwitchStatementSyntax sw, int depth, ref int maxDepth)
    {
        foreach (var section in sw.Sections)
            foreach (var stmt in section.Statements)
                VisitControlFlowNesting(stmt, depth + 1, ref maxDepth);
    }

    private static void VisitSwitchExpression(SwitchExpressionSyntax se, int depth, ref int maxDepth)
    {
        foreach (var arm in se.Arms)
            VisitControlFlowNesting(arm.Expression, depth + 1, ref maxDepth);
    }

    private static void VisitLocalFunction(LocalFunctionStatementSyntax lf, int depth, ref int maxDepth)
    {
        // Local functions DO NOT increment nesting depth — the body recurses at the
        // same depth as the declaration site. This is the distinguishing contract vs
        // other block-bearing statements (while/for/using etc).
        if (lf.Body is not null)
            VisitControlFlowNesting(lf.Body, depth, ref maxDepth);
        else if (lf.ExpressionBody is not null)
            VisitControlFlowNesting(lf.ExpressionBody.Expression, depth, ref maxDepth);
    }

    /// <summary>
    /// Handles statements whose only nested control-flow attachment is a single
    /// <c>Statement</c> child at depth+1: <see cref="WhileStatementSyntax"/>,
    /// <see cref="DoStatementSyntax"/>, <see cref="ForEachStatementSyntax"/>,
    /// <see cref="LockStatementSyntax"/>, <see cref="UsingStatementSyntax"/>,
    /// <see cref="FixedStatementSyntax"/>. Returns true when the node was handled.
    /// </summary>
    private static bool TryVisitSingleBodyAtDepthPlusOne(SyntaxNode child, int depth, ref int maxDepth)
    {
        StatementSyntax? body = child switch
        {
            WhileStatementSyntax ws => ws.Statement,
            DoStatementSyntax ds => ds.Statement,
            ForEachStatementSyntax fes => fes.Statement,
            LockStatementSyntax lk => lk.Statement,
            UsingStatementSyntax us => us.Statement,
            FixedStatementSyntax fx => fx.Statement,
            _ => null
        };

        if (body is null) return false;
        VisitControlFlowNesting(body, depth + 1, ref maxDepth);
        return true;
    }

    /// <summary>
    /// Handles statements whose only nested control-flow attachment is a single
    /// <c>Block</c> child at depth+1: <see cref="CheckedStatementSyntax"/> and
    /// <see cref="UnsafeStatementSyntax"/>. Returns true when the node was handled.
    /// </summary>
    private static bool TryVisitSingleBlockAtDepthPlusOne(SyntaxNode child, int depth, ref int maxDepth)
    {
        BlockSyntax? block = child switch
        {
            CheckedStatementSyntax chk => chk.Block,
            UnsafeStatementSyntax uns => uns.Block,
            _ => null
        };

        if (block is null) return false;
        VisitControlFlowNesting(block, depth + 1, ref maxDepth);
        return true;
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
