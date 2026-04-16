using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

public sealed class SyntaxService(IWorkspaceManager workspace) : ISyntaxService
{
    /// <summary>
    /// Caps applied during syntax-tree serialization. Three independent budgets:
    /// <list type="bullet">
    ///   <item><description><c>RemainingChars</c> — leaf-text accumulation cap (the original
    ///   <c>maxOutputChars</c> semantics). Structural JSON (kinds, positions, nesting) is NOT
    ///   counted here. Documented as such in the tool schema after the bug fix below.</description></item>
    ///   <item><description><c>RemainingNodes</c> — total node count cap (NEW, get-syntax-tree-max-output-chars-incomplete-cap).
    ///   Each emitted <see cref="SyntaxNodeDto"/> decrements this. When zero, the walker stops.</description></item>
    ///   <item><description><c>RemainingBytes</c> — total estimated response size cap (NEW). Each
    ///   emitted node consumes ~120 bytes of JSON overhead + its text length. Hard cap that
    ///   matches the agents' actual concern (response payload size) — addresses the §3 stress
    ///   test repro where <c>maxOutputChars=20000</c> produced 229 KB on EncodingHelper.cs.</description></item>
    /// </list>
    /// Hitting any one cap sets <c>Truncated=true</c> and emits a single TruncationNotice.
    /// </summary>
    private sealed class SyntaxBudget(int maxChars, int maxNodes, int maxBytes)
    {
        public int RemainingChars { get; set; } = maxChars;
        public int RemainingNodes { get; set; } = maxNodes;
        public int RemainingBytes { get; set; } = maxBytes;
        public bool Truncated { get; set; }

        /// <summary>Approximate JSON bytes per <see cref="SyntaxNodeDto"/> excluding text.</summary>
        public const int PerNodeStructuralBytes = 120;

        public bool ExhaustedAny => Truncated || RemainingChars <= 0 || RemainingNodes <= 0 || RemainingBytes <= 0;
    }

    public async Task<SyntaxNodeDto?> GetSyntaxTreeAsync(
        string workspaceId,
        string filePath,
        int? startLine,
        int? endLine,
        int maxDepth,
        CancellationToken ct,
        int maxOutputChars = 65536,
        int maxNodes = 5000,
        int maxTotalBytes = 65536)
    {
        var solution = workspace.GetCurrentSolution(workspaceId);
        var document = Helpers.SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null) return null;

        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var budget = new SyntaxBudget(maxOutputChars, maxNodes, maxTotalBytes);

        SyntaxNode targetNode = root;
        if (startLine.HasValue && endLine.HasValue)
        {
            var startIdx = Math.Max(0, Math.Min(startLine.Value - 1, text.Lines.Count - 1));
            var endIdx = Math.Max(0, Math.Min(endLine.Value - 1, text.Lines.Count - 1));
            var startPosition = text.Lines[startIdx].Start;
            var endPosition = text.Lines[endIdx].End;
            var span = TextSpan.FromBounds(startPosition, endPosition);

            var enclosingNode = root.FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: false);
            var nodesInRange = enclosingNode.ChildNodes()
                .Where(n => span.Contains(n.Span))
                .ToList();

            if (nodesInRange.Count > 0)
            {
                var children = new List<SyntaxNodeDto>();
                foreach (var n in nodesInRange)
                {
                    if (budget.ExhaustedAny)
                        break;
                    children.Add(BuildNode(n, text, 0, maxDepth, budget));
                }

                if (budget.Truncated &&
                    (children.Count == 0 || children[^1].Kind != "TruncationNotice"))
                    children.Add(CreateTruncationNotice());

                var enclosingLineSpan = enclosingNode.GetLocation().GetLineSpan();
                return FinalizeWithBudget(
                    new SyntaxNodeDto(
                        Kind: enclosingNode.Kind().ToString(),
                        Text: null,
                        StartLine: startLine.Value,
                        StartColumn: enclosingLineSpan.StartLinePosition.Character + 1,
                        EndLine: endLine.Value,
                        EndColumn: enclosingLineSpan.EndLinePosition.Character + 1,
                        Children: children),
                    budget);
            }

            targetNode = enclosingNode;
        }

        return FinalizeWithBudget(BuildNode(targetNode, text, 0, maxDepth, budget), budget);
    }

    private static SyntaxNodeDto FinalizeWithBudget(SyntaxNodeDto node, SyntaxBudget budget)
    {
        if (!budget.Truncated || node.Kind == "TruncationNotice")
            return node;

        var kids = node.Children?.ToList() ?? [];
        if (kids.Count > 0 && kids[^1].Kind == "TruncationNotice")
            return node;

        if (kids.Count == 0)
            return node with { Children = [CreateTruncationNotice()] };

        kids.Add(CreateTruncationNotice());
        return node with { Children = kids };
    }

    private static SyntaxNodeDto CreateTruncationNotice() =>
        new(
            Kind: "TruncationNotice",
            Text: "Further syntax output omitted (maxOutputChars/maxNodes/maxTotalBytes budget exceeded). Narrow startLine/endLine, lower maxDepth, or raise the relevant cap. maxOutputChars caps LEAF text only — use maxTotalBytes to cap total response size.",
            StartLine: 0,
            StartColumn: 0,
            EndLine: 0,
            EndColumn: 0,
            Children: null);

    private static SyntaxNodeDto BuildNode(SyntaxNode node, SourceText text, int depth, int maxDepth, SyntaxBudget budget)
    {
        // Each emitted node consumes one unit of node-budget AND its structural bytes.
        // Check both before recursing so the walker stops crisply at any cap.
        if (budget.ExhaustedAny)
        {
            budget.Truncated = true;
            return CreateTruncationNotice();
        }
        budget.RemainingNodes -= 1;
        budget.RemainingBytes -= SyntaxBudget.PerNodeStructuralBytes;

        var lineSpan = node.GetLocation().GetLineSpan();
        List<SyntaxNodeDto>? children = null;
        if (depth < maxDepth)
        {
            children = [];
            foreach (var child in node.ChildNodes())
            {
                if (budget.ExhaustedAny)
                {
                    budget.Truncated = true;
                    break;
                }

                children.Add(BuildNode(child, text, depth + 1, maxDepth, budget));
            }
        }

        string? nodeText = null;
        if (children is null || children.Count == 0)
        {
            nodeText = node.ToString();
            if (nodeText.Length > 500)
                nodeText = string.Concat(nodeText.AsSpan(0, 500), "\u2026");

            // Apply both leaf-text and total-byte caps. Take the smaller of the two so
            // either cap alone can truncate.
            var maxAllowed = Math.Max(0, Math.Min(budget.RemainingChars, budget.RemainingBytes));
            if (nodeText.Length > maxAllowed)
            {
                budget.Truncated = true;
                nodeText = string.Concat(nodeText.AsSpan(0, maxAllowed), "\u2026");
            }

            budget.RemainingChars -= nodeText.Length;
            budget.RemainingBytes -= nodeText.Length;
        }

        return new SyntaxNodeDto(
            Kind: node.GetType().Name.Replace("Syntax", ""),
            Text: nodeText,
            StartLine: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLine: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1,
            Children: children);
    }
}
