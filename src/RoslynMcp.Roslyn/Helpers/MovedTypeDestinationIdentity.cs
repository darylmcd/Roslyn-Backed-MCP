using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal static class MovedTypeDestinationIdentity
{
    internal static async Task EnsureAvailableAsync(
        Project originalTargetProject,
        Document projectedDocument,
        TextSpan projectedIdentifier,
        Solution finalProjection,
        TypeDeclarationSyntax? originalDeclarationInTargetProject,
        CancellationToken ct)
    {
        var root = await projectedDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("The destination syntax root is unavailable.");
        var identifier = root.FindToken(projectedIdentifier.Start);
        if (identifier.Span != projectedIdentifier || identifier.Parent is not TypeDeclarationSyntax declaration ||
            declaration.Identifier != identifier || !NamespaceMemberClassifier.IsDirectNamespaceMember(declaration))
        {
            // The emitted declaration may be inactive under the receiving project's preprocessor symbols.
            return;
        }

        var model = await projectedDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("The destination semantic model is unavailable.");
        if (model.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol movedType)
            return;

        var originalCompilation = await originalTargetProject.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("The destination compilation is unavailable.");
        var namespaceSegments = new Stack<string>();
        for (var current = movedType.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
            namespaceSegments.Push(current.Name);
        var originalNamespace = NamespaceMemberClassifier.FindNamespaceSymbol(originalCompilation.Assembly.GlobalNamespace, namespaceSegments);
        if (originalNamespace is null)
            return;

        // MetadataName accounts for arity and file-local identity. The assembly namespace excludes
        // types from references. Enumerate candidates so an existing ambiguity cannot hide them.
        foreach (var candidate in originalNamespace.GetTypeMembers(movedType.Name, movedType.Arity))
        {
            if (string.Equals(candidate.MetadataName, movedType.MetadataName, StringComparison.Ordinal) &&
                !IsOriginalDeclaration(candidate, originalDeclarationInTargetProject) &&
                await PersistsInProjectionAsync(originalTargetProject, finalProjection, namespaceSegments, candidate, ct)
                    .ConfigureAwait(false))
            {
                throw new PublicInvalidOperationException(
                    "The destination already contains a type with the same namespace, name, and arity. Choose a different destination.");
            }
        }
    }

    private static bool IsOriginalDeclaration(INamedTypeSymbol candidate, TypeDeclarationSyntax? originalDeclaration) =>
        originalDeclaration is not null && candidate.DeclaringSyntaxReferences is [var reference] &&
        reference.SyntaxTree == originalDeclaration.SyntaxTree && reference.Span == originalDeclaration.Span;

    private static async Task<bool> PersistsInProjectionAsync(
        Project originalTargetProject,
        Solution finalProjection,
        IEnumerable<string> namespaceSegments,
        INamedTypeSymbol originalType,
        CancellationToken ct)
    {
        var originalDeclarations = originalType.DeclaringSyntaxReferences;
        if (originalDeclarations.IsEmpty || originalDeclarations.Any(reference =>
                originalTargetProject.Solution.GetDocument(reference.SyntaxTree) is not SourceGeneratedDocument))
        {
            return true;
        }

        // A generated fallback can disappear when the type moves in. Any final generated
        // declaration of the original identity is distinct from the moved user declaration.
        var finalTarget = finalProjection.GetProject(originalTargetProject.Id)
            ?? throw InvalidOperationErrors.Internal("The projected destination project is unavailable.");
        var finalCompilation = await finalTarget.GetCompilationAsync(ct).ConfigureAwait(false)
            ?? throw InvalidOperationErrors.Internal("The projected destination compilation is unavailable.");
        var finalNamespace = NamespaceMemberClassifier.FindNamespaceSymbol(finalCompilation.Assembly.GlobalNamespace, namespaceSegments);
        return finalNamespace?.GetTypeMembers(originalType.Name, originalType.Arity)
            .Where(type => string.Equals(type.MetadataName, originalType.MetadataName, StringComparison.Ordinal))
            .SelectMany(type => type.DeclaringSyntaxReferences)
            .Any(reference => finalProjection.GetDocument(reference.SyntaxTree) is SourceGeneratedDocument) == true;
    }
}
