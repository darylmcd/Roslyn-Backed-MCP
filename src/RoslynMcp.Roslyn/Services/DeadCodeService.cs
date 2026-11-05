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
    private readonly Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>> _referenceFinder;

    public DeadCodeService(IWorkspaceManager workspace, IPreviewStore previewStore)
        : this(workspace, previewStore, static (symbol, solution, ct) => SymbolFinder.FindReferencesAsync(symbol, solution, ct))
    {
    }

    internal DeadCodeService(
        IWorkspaceManager workspace,
        IPreviewStore previewStore,
        Func<ISymbol, Solution, CancellationToken, Task<IEnumerable<ReferencedSymbol>>> referenceFinder)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _referenceFinder = referenceFinder;
    }

    public async Task<RefactoringPreviewDto> PreviewRemoveDeadCodeAsync(string workspaceId, DeadCodeRemovalDto request, CancellationToken ct)
    {
        if (request.SymbolHandles.Count == 0)
        {
            throw new PublicArgumentException("At least one symbol handle is required.", nameof(request));
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var updatedSolution = solution;
        var warnings = new List<string>();

        foreach (var symbolHandle in request.SymbolHandles.Distinct(StringComparer.Ordinal))
        {
            var symbol = await SymbolResolver.ResolveAsync(updatedSolution, SymbolLocator.ByHandle(symbolHandle), ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Symbol handle could not be resolved: {symbolHandle}");

            // Fail closed: a failure verifying this symbol's reference count must never be
            // treated as a legitimate zero — that would let removal proceed on an
            // unverifiable answer instead of refusing it. This makes the removal guard
            // genuinely independent of the detector's failure path (though not of a
            // detector scan that succeeds but under-counts — see UnusedCodeAnalyzer's
            // matching guard for that documented, non-eliminated residual gap).
            IEnumerable<ReferencedSymbol> refs;
            try
            {
                refs = await _referenceFinder(symbol, updatedSolution, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new PublicInvalidOperationException(
                    $"Cannot verify '{symbol.Name}' has zero references — the verification scan failed; refusing removal.",
                    ex);
            }

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
            // XML doc comments). An assembly attribute or a declaration disabled by `#if` is
            // real content that a member walk cannot see, so the shared classifier checks both
            // (the same decision the namespace relocation and move-to-project previews make).
            var fileHasNoDeclarations = candidateRoot is not null && !NamespaceMemberClassifier.HasRemainingContent(candidateRoot);

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
        // preview-token-apply-route-provenance: tag the producer family so remove_dead_code_apply
        // can refuse a foreign token before mutating the workspace.
        var token = _previewStore.Store(workspaceId, updatedSolution, _workspace.GetCurrentVersion(workspaceId), "Remove dead code symbols", changes, PreviewKind.RemoveDeadCode);
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
