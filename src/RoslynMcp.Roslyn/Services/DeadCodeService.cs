using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Roslyn.Services;

public sealed class DeadCodeService : IDeadCodeService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;

    public DeadCodeService(IWorkspaceManager workspace, IPreviewStore previewStore)
    {
        _workspace = workspace;
        _previewStore = previewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewRemoveDeadCodeAsync(string workspaceId, DeadCodeRemovalDto request, CancellationToken ct)
    {
        if (request.SymbolHandles.Count == 0)
        {
            throw new ArgumentException("At least one symbol handle is required.", nameof(request));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var updatedSolution = solution;
        var warnings = new List<string>();

        foreach (var symbolHandle in request.SymbolHandles.Distinct(StringComparer.Ordinal))
        {
            var symbol = await SymbolResolver.ResolveAsync(updatedSolution, SymbolLocator.ByHandle(symbolHandle), ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Symbol handle could not be resolved: {symbolHandle}");

            var refs = await SymbolFinder.FindReferencesAsync(symbol, updatedSolution, ct).ConfigureAwait(false);
            if (refs.Sum(reference => reference.Locations.Count()) > 0)
            {
                throw new InvalidOperationException($"Symbol '{symbol.Name}' still has references and cannot be removed safely.");
            }

            var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource)
                ?? throw new InvalidOperationException($"Symbol '{symbol.Name}' does not have a source location.");
            var document = updatedSolution.GetDocument(sourceLocation.SourceTree)
                ?? throw new InvalidOperationException($"Document was not found for symbol '{symbol.Name}'.");
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Syntax root could not be loaded for '{document.Name}'.");
            var targetNode = root.FindNode(sourceLocation.SourceSpan, getInnermostNodeForTie: true);
            var updatedRoot = RemoveDeclaration(root, targetNode)
                ?? throw new InvalidOperationException($"Symbol '{symbol.Name}' cannot be removed by the dead-code tool.");

            var candidateDocument = document.WithSyntaxRoot(updatedRoot);
            var candidateRoot = await candidateDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            updatedSolution = candidateDocument.Project.Solution;

            if (request.RemoveEmptyFiles && candidateRoot is not null && !candidateRoot.DescendantNodes().OfType<MemberDeclarationSyntax>().Any())
            {
                updatedSolution = updatedSolution.RemoveDocument(document.Id);
            }
            else if (!request.RemoveEmptyFiles && candidateRoot is not null && !candidateRoot.DescendantNodes().OfType<MemberDeclarationSyntax>().Any())
            {
                warnings.Add($"Removing '{symbol.Name}' leaves '{document.Name}' empty.");
            }
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), "Remove dead code symbols");
        return new RefactoringPreviewDto(token, "Remove dead code symbols", changes, warnings.Count > 0 ? warnings : null);
    }

    private static SyntaxNode? RemoveDeclaration(SyntaxNode root, SyntaxNode targetNode)
    {
        var member = targetNode.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (member is not null)
        {
            return root.RemoveNode(member, SyntaxRemoveOptions.KeepExteriorTrivia);
        }

        if (targetNode is VariableDeclaratorSyntax variableDeclarator &&
            variableDeclarator.Parent?.Parent is FieldDeclarationSyntax fieldDeclaration)
        {
            if (fieldDeclaration.Declaration.Variables.Count == 1)
            {
                return root.RemoveNode(fieldDeclaration, SyntaxRemoveOptions.KeepExteriorTrivia);
            }

            var updatedField = fieldDeclaration.RemoveNode(variableDeclarator, SyntaxRemoveOptions.KeepExteriorTrivia);
            return updatedField is null ? null : root.ReplaceNode(fieldDeclaration, updatedField);
        }

        return null;
    }
}