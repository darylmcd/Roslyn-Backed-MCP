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
        var (firstStatement, lastStatement, semanticModel) = await ResolveStatementRangeAsync(
            workspaceId, filePath, startLine, endLine, ct).ConfigureAwait(false);

        var result = semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);

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
        var (firstStatement, lastStatement, semanticModel) = await ResolveStatementRangeAsync(
            workspaceId, filePath, startLine, endLine, ct).ConfigureAwait(false);

        var result = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);

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

        return new ControlFlowAnalysisDto(
            Succeeded: result.Succeeded,
            StartPointIsReachable: result.StartPointIsReachable,
            EndPointIsReachable: result.EndPointIsReachable,
            EntryPoints: entryPoints,
            ExitPoints: exitPoints,
            ReturnStatements: returnStatements);
    }

    private async Task<(SyntaxNode First, SyntaxNode Last, SemanticModel Model)> ResolveStatementRangeAsync(
        string workspaceId, string filePath, int startLine, int endLine, CancellationToken ct)
    {
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
            .Where(n => n is StatementSyntax && !(n is BlockSyntax))
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
            throw new InvalidOperationException(
                $"No statements found in the line range {startLine}-{endLine}. " +
                "Ensure the range contains executable statements (not just declarations or braces).");
        }

        return (statements.First(), statements.Last(), semanticModel);
    }

    private static IReadOnlyList<string> SymbolNames(
        System.Collections.Immutable.ImmutableArray<ISymbol> symbols) =>
        symbols.Select(s => s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)).ToList();

    private static string FormatSyntaxLocation(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        return $"Line {lineSpan.StartLinePosition.Line + 1}: {node.ToString().Trim()}";
    }
}
