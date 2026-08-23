using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Roslyn.Helpers;

internal static class SymbolServiceHelpers
{
    public static async Task<ISymbol?> GetContainingSymbolAsync(Document document, Location location, CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return null;

        return GetContainingSymbolFromRoot(root, semanticModel, location, ct);
    }

    public static ISymbol? GetContainingSymbolFromRoot(SyntaxNode root, SemanticModel model, Location location, CancellationToken ct)
    {
        var node = root.FindNode(location.SourceSpan);
        while (node is not null)
        {
            if (node is MemberDeclarationSyntax or LocalFunctionStatementSyntax)
            {
                return model.GetDeclaredSymbol(node, ct);
            }
            node = node.Parent;
        }
        return null;
    }

    public static async Task<IReadOnlyList<RoslynMcp.Core.Models.LocationDto>> SymbolsToLocationsAsync(
        IEnumerable<ISymbol> symbols,
        Solution solution,
        CancellationToken ct)
    {
        var results = new List<RoslynMcp.Core.Models.LocationDto>();
        foreach (var symbol in symbols.Distinct(SymbolEqualityComparer.Default))
        {
            foreach (var location in symbol.Locations.Where(location => location.IsInSource))
            {
                var document = solution.GetDocument(location.SourceTree!);
                var preview = document is not null ? await SymbolResolver.GetPreviewTextAsync(document, location, ct).ConfigureAwait(false) : null;
                results.Add(SymbolMapper.ToLocationDto(location, symbol, preview));
            }
        }

        return results;
    }

    public static IEnumerable<ISymbol> GetBaseMembers(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => EnumerateOverrideBases(method, m => m.OverriddenMethod, method.ExplicitInterfaceImplementations),
            IPropertySymbol property => EnumerateOverrideBases(property, p => p.OverriddenProperty, property.ExplicitInterfaceImplementations),
            IEventSymbol eventSymbol => EnumerateOverrideBases(eventSymbol, e => e.OverriddenEvent, eventSymbol.ExplicitInterfaceImplementations),
            INamedTypeSymbol namedType => EnumerateTypeBases(namedType),
            _ => []
        };
    }

    // Walk override + interface-implementation bases for an overridable member (method, property,
    // or event). C# exposes no common IOverridableSymbol, so the per-kind Overridden* accessor is
    // passed as a selector and the kind-typed ExplicitInterfaceImplementations as a sequence.
    private static IEnumerable<ISymbol> EnumerateOverrideBases<T>(
        T symbol,
        Func<T, T?> overriddenSelector,
        IEnumerable<T> explicitImplementations)
        where T : class, ISymbol
    {
        var current = overriddenSelector(symbol);
        while (current is not null)
        {
            yield return current;
            current = overriddenSelector(current);
        }

        foreach (var explicitImplementation in explicitImplementations)
        {
            yield return explicitImplementation;
        }

        // Interface implementations: yield any interface member this symbol implements.
        foreach (var interfaceMember in GetImplementedInterfaceMembers(symbol))
        {
            yield return interfaceMember;
        }
    }

    /// <summary>
    /// Enumerates interface members implemented by <paramref name="symbol"/> using Roslyn's
    /// authoritative implementation mapping. The ordering follows <see cref="INamedTypeSymbol.AllInterfaces"/>
    /// and each interface's member order.
    /// </summary>
    public static IEnumerable<T> GetImplementedInterfaceMembers<T>(T symbol)
        where T : class, ISymbol
    {
        var containingType = symbol.ContainingType;
        if (containingType is null || containingType.TypeKind == TypeKind.Interface)
        {
            yield break;
        }

        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var interfaceMember in iface.GetMembers().OfType<T>())
            {
                var implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, symbol))
                {
                    yield return interfaceMember;
                }
            }
        }
    }

    private static IEnumerable<ISymbol> EnumerateTypeBases(INamedTypeSymbol namedType)
    {
        var current = namedType.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            yield return current;
            current = current.BaseType;
        }

        foreach (var interfaceSymbol in namedType.Interfaces)
        {
            yield return interfaceSymbol;
        }
    }
}
