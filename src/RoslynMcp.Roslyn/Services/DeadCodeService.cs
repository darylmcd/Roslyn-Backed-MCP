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

            // UX-005: A file is considered "effectively empty" when it contains no real
            // declarations — only namespace shells, using directives, and trivia (whitespace,
            // XML doc comments). Previously this was checked with `OfType<MemberDeclarationSyntax>()`
            // which counts NamespaceDeclarationSyntax as a member, so files containing nothing but
            // an empty namespace (or an orphaned doc comment) were never deleted even with
            // removeEmptyFiles=true. The new helper unwraps namespaces and ignores trivia.
            var fileHasNoDeclarations = candidateRoot is not null && IsEffectivelyEmpty(candidateRoot);

            if (request.RemoveEmptyFiles && fileHasNoDeclarations)
            {
                updatedSolution = updatedSolution.RemoveDocument(document.Id);
            }
            else if (!request.RemoveEmptyFiles && fileHasNoDeclarations)
            {
                warnings.Add($"Removing '{symbol.Name}' leaves '{document.Name}' empty.");
            }
        }

        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, updatedSolution, ct).ConfigureAwait(false);
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), "Remove dead code symbols", changes);
        return new RefactoringPreviewDto(token, "Remove dead code symbols", changes, warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the syntax root has no real declarations left after a
    /// removal — i.e., everything that remains is namespace shells, using directives, or trivia.
    /// </summary>
    private static bool IsEffectivelyEmpty(SyntaxNode root)
    {
        // Strip all NamespaceDeclarationSyntax / FileScopedNamespaceDeclarationSyntax wrappers and
        // check whether any non-namespace MemberDeclarationSyntax survived. A namespace counts as
        // empty when none of its descendants are real members.
        var realMembers = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(m => m is not BaseNamespaceDeclarationSyntax);
        return !realMembers.Any();
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
