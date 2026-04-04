using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

public sealed class SyntaxService(IWorkspaceManager workspace) : ISyntaxService
{
    private sealed class SyntaxBudget(int maxChars)
    {
        public int Remaining { get; set; } = maxChars;
        public bool Truncated { get; set; }
    }

    public async Task<SyntaxNodeDto?> GetSyntaxTreeAsync(
        string workspaceId,
        string filePath,
        int? startLine,
        int? endLine,
        int maxDepth,
        CancellationToken ct,
        int maxOutputChars = 65536)
    {
        var solution = workspace.GetCurrentSolution(workspaceId);
        var document = Helpers.SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null) return null;

        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var budget = new SyntaxBudget(maxOutputChars);

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
                    if (budget.Truncated || budget.Remaining <= 0)
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
            Text: "Further syntax output omitted (maxOutputChars budget exceeded). Narrow the line range, lower maxDepth, or increase maxOutputChars on the tool.",
            StartLine: 0,
            StartColumn: 0,
            EndLine: 0,
            EndColumn: 0,
            Children: null);

    private static SyntaxNodeDto BuildNode(SyntaxNode node, SourceText text, int depth, int maxDepth, SyntaxBudget budget)
    {
        if (budget.Remaining <= 0)
        {
            budget.Truncated = true;
            return CreateTruncationNotice();
        }

        var lineSpan = node.GetLocation().GetLineSpan();
        List<SyntaxNodeDto>? children = null;
        if (depth < maxDepth)
        {
            children = [];
            foreach (var child in node.ChildNodes())
            {
                if (budget.Remaining <= 0)
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

            var cost = nodeText.Length;
            if (cost > budget.Remaining)
            {
                budget.Truncated = true;
                nodeText = string.Concat(nodeText.AsSpan(0, Math.Max(0, budget.Remaining)), "\u2026");
                cost = nodeText.Length;
            }

            budget.Remaining -= cost;
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
