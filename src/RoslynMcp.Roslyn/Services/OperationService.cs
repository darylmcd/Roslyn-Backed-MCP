using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class OperationService : IOperationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<OperationService> _logger;

    public OperationService(IWorkspaceManager workspace, ILogger<OperationService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<OperationNodeDto?> GetOperationsAsync(
        string workspaceId, string filePath, int line, int column, int maxDepth, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new FileNotFoundException($"Document not found in workspace: {filePath}");

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get syntax tree.");
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not get semantic model.");
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        // Convert 1-based to 0-based and find the position
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var position = text.Lines[line - 1].Start + (column - 1);

        // Find the most specific node at this position
        var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));
        if (node is null) return null;

        // Walk up to find a node that has an IOperation
        IOperation? operation = null;
        var current = node;
        while (current is not null)
        {
            operation = semanticModel.GetOperation(current, ct);
            if (operation is not null) break;
            current = current.Parent;
        }

        if (operation is null) return null;

        return MapOperation(operation, maxDepth, 0);
    }

    private static OperationNodeDto MapOperation(IOperation operation, int maxDepth, int currentDepth)
    {
        var lineSpan = operation.Syntax.GetLocation().GetLineSpan();
        var syntaxText = operation.Syntax.ToString();

        // Truncate long syntax text
        if (syntaxText.Length > 120)
            syntaxText = syntaxText[..117] + "...";

        IReadOnlyList<OperationNodeDto>? children = null;

        if (currentDepth < maxDepth)
        {
            var childOps = operation.ChildOperations.ToList();
            if (childOps.Count > 0)
            {
                children = childOps
                    .Select(child => MapOperation(child, maxDepth, currentDepth + 1))
                    .ToList();
            }
        }

        return new OperationNodeDto(
            Kind: operation.Kind.ToString(),
            Type: operation.Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ConstantValue: operation.ConstantValue.HasValue ? operation.ConstantValue.Value?.ToString() : null,
            Syntax: syntaxText,
            Line: lineSpan.StartLinePosition.Line + 1,
            Column: lineSpan.StartLinePosition.Character + 1,
            Children: children);
    }
}
