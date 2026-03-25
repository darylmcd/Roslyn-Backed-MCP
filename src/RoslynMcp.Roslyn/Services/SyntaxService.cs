using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
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
            var startPosition = text.Lines[Math.Max(0, startLine.Value - 1)].Start;
            var endPosition = text.Lines[Math.Min(text.Lines.Count - 1, endLine.Value - 1)].End;
            var span = TextSpan.FromBounds(startPosition, endPosition);
            targetNode = root.FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: false);
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
