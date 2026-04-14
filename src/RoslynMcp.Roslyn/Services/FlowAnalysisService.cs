using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class FlowAnalysisService : IFlowAnalysisService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<FlowAnalysisService> _logger;

    public FlowAnalysisService(IWorkspaceManager workspace, ILogger<FlowAnalysisService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<DataFlowAnalysisDto> AnalyzeDataFlowAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct)
    {
        var resolution = await ResolveAnalysisRegionAsync(
            workspaceId, filePath, startLine, endLine, ct).ConfigureAwait(false);

        DataFlowAnalysis result;
        try
        {
            // Lift `=> expr` (expression-bodied member) to a single-expression data-flow
            // analysis. Roslyn's SemanticModel.AnalyzeDataFlow(ExpressionSyntax) handles the
            // expression directly without needing a synthetic enclosing block, so we can
            // satisfy the analysis without rewriting the syntax tree.
            result = resolution.ExpressionBody is not null
                ? resolution.Model.AnalyzeDataFlow(resolution.ExpressionBody)
                : resolution.Model.AnalyzeDataFlow(resolution.FirstStatement!, resolution.LastStatement!);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Data flow analysis failed for lines {startLine}-{endLine}: {ex.Message}. " +
                "Try widening the range to a complete expression-bodied member or to statements within a single block (avoid spanning try/catch/finally boundaries).", ex);
        }

        return new DataFlowAnalysisDto(
            Succeeded: result.Succeeded,
            VariablesDeclared: SymbolNames(result.VariablesDeclared),
            DataFlowsIn: SymbolNames(result.DataFlowsIn),
            DataFlowsOut: SymbolNames(result.DataFlowsOut),
            AlwaysAssigned: SymbolNames(result.AlwaysAssigned),
            ReadInside: SymbolNames(result.ReadInside),
            WrittenInside: SymbolNames(result.WrittenInside),
            ReadOutside: SymbolNames(result.ReadOutside),
            WrittenOutside: SymbolNames(result.WrittenOutside),
            Captured: SymbolNames(result.Captured),
            CapturedInside: SymbolNames(result.CapturedInside),
            UnsafeAddressTaken: SymbolNames(result.UnsafeAddressTaken));
    }

    public async Task<ControlFlowAnalysisDto> AnalyzeControlFlowAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct)
    {
        var resolution = await ResolveAnalysisRegionAsync(
            workspaceId, filePath, startLine, endLine, ct).ConfigureAwait(false);

        // Roslyn does not expose an AnalyzeControlFlow overload for expression-bodied members.
        // Synthesize the trivially-correct result for a `=> expr` body: no entry/exit points,
        // no goto/break/continue, exactly one implicit return at the arrow location, end point
        // not reachable (the implicit return exits the method).
        if (resolution.ExpressionBody is not null)
        {
            var lineSpan = resolution.ExpressionBody.GetLocation().GetLineSpan();
            var implicitReturn = new ReturnStatementDto(
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1,
                resolution.ExpressionBody.ToString());

            return new ControlFlowAnalysisDto(
                Succeeded: true,
                StartPointIsReachable: true,
                EndPointIsReachable: false,
                EntryPoints: [],
                ExitPoints: [],
                ReturnStatements: [implicitReturn],
                Warning: "Synthesized from expression-bodied member: no statements to traverse, only the implicit return of the expression value.");
        }

        ControlFlowAnalysis result;
        try
        {
            result = resolution.Model.AnalyzeControlFlow(resolution.FirstStatement!, resolution.LastStatement!);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Control flow analysis failed for lines {startLine}-{endLine}: {ex.Message}. " +
                "Try widening the range to a complete expression-bodied member or to statements within a single block (avoid spanning try/catch/finally boundaries).", ex);
        }

        var entryPoints = result.EntryPoints
            .Select(n => FormatSyntaxLocation(n))
            .ToList();

        var exitPoints = result.ExitPoints
            .Select(n => FormatSyntaxLocation(n))
            .ToList();

        var returnStatements = result.ReturnStatements
            .Select(n =>
            {
                var lineSpan = n.GetLocation().GetLineSpan();
                var expressionText = n is ReturnStatementSyntax rs ? rs.Expression?.ToString() : n.ToString();
                return new ReturnStatementDto(
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.StartLinePosition.Character + 1,
                    expressionText);
            })
            .ToList();

        string? warning = null;
        if (!result.Succeeded ||
            (entryPoints.Count == 0 && exitPoints.Count == 0 && returnStatements.Count == 0))
        {
            warning =
                "Control-flow results may be incomplete for this line range. Prefer a range that covers full statement blocks within a single method body (not a partial slice of a method).";
        }
        else if (!result.EndPointIsReachable &&
                 (returnStatements.Count > 0 || exitPoints.Count > 0))
        {
            warning =
                "EndPointIsReachable is false because Roslyn models whether execution can fall through the end of the analyzed region without an explicit return/throw — not whether the method can complete. " +
                "If return/exit points are listed, the region still exits via those paths.";
        }

        return new ControlFlowAnalysisDto(
            Succeeded: result.Succeeded,
            StartPointIsReachable: result.StartPointIsReachable,
            EndPointIsReachable: result.EndPointIsReachable,
            EntryPoints: entryPoints,
            ExitPoints: exitPoints,
            ReturnStatements: returnStatements,
            Warning: warning);
    }

    /// <summary>
    /// Resolution result for a flow-analysis request: either a statement range
    /// (<see cref="FirstStatement"/> + <see cref="LastStatement"/>) or an expression body
    /// (<see cref="ExpressionBody"/>) lifted from a `=> expr` member. Exactly one of the
    /// two cases is populated.
    /// </summary>
    private sealed record AnalysisRegion(
        SyntaxNode? FirstStatement,
        SyntaxNode? LastStatement,
        ExpressionSyntax? ExpressionBody,
        SemanticModel Model);

    private async Task<AnalysisRegion> ResolveAnalysisRegionAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct)
    {
        // analyze-data-flow-inverted-range: validate range orientation upfront so callers get
        // a structured InvalidArgument error instead of the misleading "No statements found in
        // the line range N-M" InvalidOperation message that resulted from the inverted predicate.
        if (startLine < 1)
            throw new ArgumentException($"startLine ({startLine}) must be >= 1.", nameof(startLine));
        if (endLine < 1)
            throw new ArgumentException($"endLine ({endLine}) must be >= 1.", nameof(endLine));
        if (startLine > endLine)
            throw new ArgumentException(
                $"startLine ({startLine}) must be <= endLine ({endLine}).", nameof(startLine));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var normalizedPath = Path.GetFullPath(filePath);

        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath is not null &&
                Path.GetFullPath(d.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Document not found in workspace: {filePath}");

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get syntax tree.");
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get semantic model.");
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        // Convert 1-based lines to 0-based
        int startLine0 = startLine - 1;
        int endLine0 = endLine - 1;

        // Find all statements that overlap the requested line range
        var statements = root.DescendantNodes()
            .Where(n => n is StatementSyntax && n is not BlockSyntax)
            .Where(n =>
            {
                var lineSpan = n.GetLocation().GetLineSpan();
                return lineSpan.StartLinePosition.Line >= startLine0 &&
                       lineSpan.StartLinePosition.Line <= endLine0;
            })
            .OrderBy(n => n.SpanStart)
            .ToList();

        if (statements.Count == 0)
        {
            // No statements found — fall back to the expression-bodied member lift before
            // surfacing an error. Common case from the 2026-04-07 FirewallAnalyzer audit:
            // a 14-line `=> expr` method like `DriftDetector.RulesEqual` that callers want
            // to flow-analyze without rewriting the source.
            var arrow = root.DescendantNodes()
                .OfType<ArrowExpressionClauseSyntax>()
                .FirstOrDefault(n =>
                {
                    var lineSpan = n.GetLocation().GetLineSpan();
                    return lineSpan.StartLinePosition.Line >= startLine0 - 1 &&
                           lineSpan.StartLinePosition.Line <= endLine0 + 1;
                });

            if (arrow is not null)
            {
                return new AnalysisRegion(
                    FirstStatement: null,
                    LastStatement: null,
                    ExpressionBody: arrow.Expression,
                    Model: semanticModel);
            }

            throw new InvalidOperationException(
                $"No statements found in the line range {startLine}-{endLine}. " +
                "Ensure the range contains executable statements or a `=> expr` expression body.");
        }

        // AnalyzeDataFlow/ControlFlow requires first and last to share the same BlockSyntax parent.
        // When the range spans try-catch or multiple blocks, find the largest contiguous group
        // within a single block.
        var first = statements.First();
        var last = statements.Last();

        if (!ShareCommonBlock(first, last))
        {
            // Group statements by their immediate block parent and pick the largest group
            var groups = statements
                .GroupBy(s => s.Parent)
                .Where(g => g.Key is BlockSyntax)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (groups is null)
            {
                throw new InvalidOperationException(
                    $"Statements in the line range {startLine}-{endLine} span multiple blocks " +
                    "(e.g., try and catch). Narrow the range to statements within a single block.");
            }

            var blockStatements = groups.OrderBy(s => s.SpanStart).ToList();
            first = blockStatements.First();
            last = blockStatements.Last();
        }

        return new AnalysisRegion(
            FirstStatement: first,
            LastStatement: last,
            ExpressionBody: null,
            Model: semanticModel);
    }

    private static bool ShareCommonBlock(SyntaxNode a, SyntaxNode b)
    {
        var blockA = a.Parent as BlockSyntax;
        var blockB = b.Parent as BlockSyntax;
        return blockA is not null && ReferenceEquals(blockA, blockB);
    }

    private static IReadOnlyList<string> SymbolNames(
        System.Collections.Immutable.ImmutableArray<ISymbol> symbols)
    {
        var names = symbols
            .Select(s => s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToList();
        var dupNames = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        var result = new List<string>(symbols.Length);
        for (var i = 0; i < symbols.Length; i++)
        {
            var sym = symbols[i];
            var name = names[i];
            if (!dupNames.Contains(name))
            {
                result.Add(name);
                continue;
            }

            var loc = sym.Locations.FirstOrDefault(l => l.IsInSource);
            var line = loc?.GetLineSpan().StartLinePosition.Line + 1 ?? 0;
            result.Add($"{name} (decl line {line})");
        }

        return result;
    }

    private static string FormatSyntaxLocation(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        return $"Line {lineSpan.StartLinePosition.Line + 1}: {node.ToString().Trim()}";
    }
}
