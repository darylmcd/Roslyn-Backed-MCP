using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Services;

public sealed class SyntaxService(IWorkspaceManager workspace) : ISyntaxService
{
    public async Task<SyntaxNodeDto?> GetSyntaxTreeAsync(string workspaceId, string filePath, int? startLine, int? endLine, int maxDepth, CancellationToken ct)
    {
        var solution = workspace.GetCurrentSolution(workspaceId);
        var document = Helpers.SymbolResolver.FindDocument(solution, filePath);
        if (document is null) return null;

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null) return null;

        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        SyntaxNode targetNode = root;
        if (startLine.HasValue && endLine.HasValue)
        {
            var startIdx = Math.Max(0, Math.Min(startLine.Value - 1, text.Lines.Count - 1));
            var endIdx = Math.Max(0, Math.Min(endLine.Value - 1, text.Lines.Count - 1));
            var startPosition = text.Lines[startIdx].Start;
            var endPosition = text.Lines[endIdx].End;
            var span = TextSpan.FromBounds(startPosition, endPosition);

            // FindNode returns the smallest enclosing node (often the entire class).
            // Instead, find nodes whose spans are contained within the requested range.
            var enclosingNode = root.FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: false);
            var nodesInRange = enclosingNode.ChildNodes()
                .Where(n => span.Contains(n.Span))
                .ToList();

            if (nodesInRange.Count > 0)
            {
                // Build a virtual container showing only the nodes within the range
                var children = nodesInRange
                    .Select(n => BuildNode(n, text, 0, maxDepth))
                    .ToList();
                var lineSpan = enclosingNode.GetLocation().GetLineSpan();
                var enclosingLineSpan = enclosingNode.GetLocation().GetLineSpan();
                return new SyntaxNodeDto(
                    Kind: enclosingNode.Kind().ToString(),
                    Text: null,
                    StartLine: startLine.Value,
                    StartColumn: enclosingLineSpan.StartLinePosition.Character + 1,
                    EndLine: endLine.Value,
                    EndColumn: enclosingLineSpan.EndLinePosition.Character + 1,
                    Children: children);
            }

            targetNode = enclosingNode;
        }

        return BuildNode(targetNode, text, 0, maxDepth);
    }

    private static SyntaxNodeDto BuildNode(SyntaxNode node, SourceText text, int depth, int maxDepth)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var children = depth < maxDepth
            ? node.ChildNodes().Select(c => BuildNode(c, text, depth + 1, maxDepth)).ToList()
            : null;

        var nodeText = (children is null || children.Count == 0) ? node.ToString() : null;
        if (nodeText is not null && nodeText.Length > 500)
            nodeText = string.Concat(nodeText.AsSpan(0, 500), "\u2026");

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
