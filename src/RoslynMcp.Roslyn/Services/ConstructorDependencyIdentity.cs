using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Identifies the selected declaration across source and imported compiler views.</summary>
internal sealed class ConstructorDependencyIdentity
{
    private readonly Solution _solution;
    internal INamedTypeSymbol Type { get; }

    private ConstructorDependencyIdentity(Solution solution, INamedTypeSymbol type)
    {
        _solution = solution;
        Type = type;
    }

    internal static async Task<ConstructorDependencyIdentity> CreateAsync(
        Document sourceDocument, SyntaxAnnotation declarationAnnotation, CancellationToken ct)
    {
        var root = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var declaration = root?.GetAnnotatedNodes(declarationAnnotation).OfType<TypeDeclarationSyntax>().SingleOrDefault();
        var model = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (declaration is null || model?.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol type)
            throw new PublicInvalidOperationException("The selected source type could not be resolved after interface extraction.");
        return new(sourceDocument.Project.Solution, type);
    }

    internal async Task<bool> MatchesAsync(ITypeSymbol? candidate, CancellationToken ct)
    {
        if (candidate is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error
            || named.ContainingAssembly is not { } assembly || !Equals(Type.ContainingAssembly.Identity, assembly.Identity))
            return false;
        if (SymbolEqualityComparer.Default.Equals(Type, named)) return true;
        var source = await SymbolFinder.FindSourceDefinitionAsync(named, _solution, ct).ConfigureAwait(false);
        return source is INamedTypeSymbol && SymbolEqualityComparer.Default.Equals(Type, source);
    }

    internal static bool IsConstructorParameter(ParameterSyntax parameter) =>
        parameter.Parent is ParameterListSyntax { Parent: ConstructorDeclarationSyntax or TypeDeclarationSyntax };
}
